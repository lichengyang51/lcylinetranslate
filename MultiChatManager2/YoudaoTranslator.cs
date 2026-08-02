using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MultiChatManager2
{
    public sealed class YoudaoTranslator : IDisposable
    {
        private const string ApiUrl =
            "https://openapi.youdao.com/api";

        private readonly HttpClient _httpClient;

        private readonly string _configPath;

        private readonly ConcurrentDictionary<string, string>
            _translationCache =
                new(StringComparer.Ordinal);

        /*
         * 同一段日文有可能在 LINE 页面重绘时被重复发现，或同时出现在
         * 两个账号窗口中。这里让相同文本共用同一个正在执行的翻译任务，
         * 避免重复扣除字符和并发请求。
         */
        private readonly ConcurrentDictionary<
            string,
            Lazy<Task<string?>>>
            _inflightTranslations =
                new(StringComparer.Ordinal);

        private const int MaximumCachedTranslations = 500;

        private YoudaoConfig? _config;

        public YoudaoTranslator(
            string dataFolder)
        {
            Directory.CreateDirectory(
                dataFolder);

            _configPath =
                Path.Combine(
                    dataFolder,
                    "youdao.json");

            _httpClient =
                new HttpClient
                {
                    Timeout =
                        TimeSpan.FromSeconds(20)
                };
        }

        public bool IsConfigured =>
            _config != null &&
            !string.IsNullOrWhiteSpace(
                _config.AppKey) &&
            !string.IsNullOrWhiteSpace(
                _config.AppSecret);

        public string ConfigPath =>
            _configPath;

        public void ClearTranslationCache()
        {
            /*
             * 缓存只保存在当前软件运行期间；不会删除有道 Key、LINE
             * 登录状态或任何聊天记录。进行中的请求仍保留，以免重复请求。
             */
            _translationCache.Clear();
        }

        public YoudaoConfig GetConfiguration()
        {
            return new YoudaoConfig
            {
                AppKey =
                    _config?.AppKey ??
                    string.Empty,

                AppSecret =
                    _config?.AppSecret ??
                    string.Empty
            };
        }

        public void SaveConfiguration(
            string appKey,
            string appSecret)
        {
            string normalizedAppKey =
                appKey.Trim();

            string normalizedAppSecret =
                appSecret.Trim();

            if (string.IsNullOrWhiteSpace(
                    normalizedAppKey))
            {
                throw new ArgumentException(
                    "请输入有道 AppKey。",
                    nameof(appKey));
            }

            if (string.IsNullOrWhiteSpace(
                    normalizedAppSecret))
            {
                throw new ArgumentException(
                    "请输入有道 AppSecret。",
                    nameof(appSecret));
            }

            YoudaoConfig config =
                new YoudaoConfig
                {
                    AppKey =
                        normalizedAppKey,

                    AppSecret =
                        normalizedAppSecret
                };

            string json =
                JsonSerializer.Serialize(
                    config,
                    new JsonSerializerOptions
                    {
                        WriteIndented =
                            true
                    });

            File.WriteAllText(
                _configPath,
                json,
                Encoding.UTF8);

            _config =
                config;

            _translationCache.Clear();
        }

        public void ClearConfiguration()
        {
            _config =
                null;

            _translationCache.Clear();

            if (File.Exists(
                    _configPath))
            {
                File.Delete(
                    _configPath);
            }
        }
        public void LoadConfiguration()
        {
            if (!File.Exists(
                    _configPath))
            {
                _config =
                    null;

                return;
            }

            string json =
                File.ReadAllText(
                    _configPath,
                    Encoding.UTF8);

            _config =
                JsonSerializer.Deserialize<YoudaoConfig>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive =
                            true
                    });
        }

        public Task<string?> TranslateJapaneseToChineseAsync(
            string sourceText,
            CancellationToken cancellationToken =
                default,
            bool forceRefresh = false,
            IProgress<TranslationProgress>? progress =
                null)
        {
            string text =
                NormalizeText(
                    sourceText);

            if (string.IsNullOrWhiteSpace(text) ||
                !ContainsJapanese(text))
            {
                return Task.FromResult<string?>(null);
            }

            if (!forceRefresh &&
                _translationCache.TryGetValue(
                    text,
                    out string? cachedTranslation))
            {
                return Task.FromResult<string?>(
                    cachedTranslation);
            }

            if (forceRefresh)
            {
                /* 手动 ↻ 明确要求重新请求，不沿用旧译文。 */
                _translationCache.TryRemove(
                    text,
                    out _);
            }

            return GetSharedTranslationAsync(
                text,
                cancellationToken,
                forceRefresh,
                progress);
        }

        private async Task<string?> GetSharedTranslationAsync(
            string text,
            CancellationToken cancellationToken,
            bool forceRefresh,
            IProgress<TranslationProgress>? progress)
        {
            /* 已完成的任务不应长期占用字典；下一次访问时顺手移除。 */
            if (_inflightTranslations.TryGetValue(
                    text,
                    out Lazy<Task<string?>>? previous) &&
                previous.IsValueCreated &&
                previous.Value.IsCompleted)
            {
                _inflightTranslations.TryRemove(
                    text,
                    out _);
            }

            Lazy<Task<string?>> requested =
                new(
                    () => TranslateCoreAsync(
                        text,
                        CancellationToken.None,
                        forceRefresh,
                        progress),
                    LazyThreadSafetyMode.ExecutionAndPublication);

            Lazy<Task<string?>> shared =
                _inflightTranslations.GetOrAdd(
                    text,
                    requested);

            try
            {
                /* 一个界面取消等待时，不能把另一个界面的请求一起取消。 */
                return await shared.Value.WaitAsync(
                    cancellationToken);
            }
            finally
            {
                if (shared.IsValueCreated &&
                    shared.Value.IsCompleted &&
                    _inflightTranslations.TryGetValue(
                        text,
                        out Lazy<Task<string?>>? current) &&
                    ReferenceEquals(
                        current,
                        shared))
                {
                    _inflightTranslations.TryRemove(
                        text,
                        out _);
                }
            }
        }

        private async Task<string?> TranslateCoreAsync(
            string sourceText,
            CancellationToken cancellationToken,
            bool forceRefresh,
            IProgress<TranslationProgress>? progress)
        {
            string text =
                NormalizeText(
                    sourceText);

            if (string.IsNullOrWhiteSpace(
                    text))
            {
                return null;
            }

            if (!ContainsJapanese(
                    text))
            {
                return null;
            }

            if (!forceRefresh &&
                _translationCache.TryGetValue(
                    text,
                    out string? cachedTranslation))
            {
                return cachedTranslation;
            }

            /*
             * 有道接口虽然允许较长文本，但聊天记录中的超长气泡
             * 容易出现只返回部分内容或请求超时。按自然断句切开后
             * 分别翻译，再合并成一条完整译文。
             */
            if (text.Length > 1800)
            {
                List<string> sourceParts =
                    SplitLongText(
                        text,
                        1600)
                    .ToList();

                List<string> translatedParts =
                    new();

                for (int index = 0;
                     index < sourceParts.Count;
                     index++)
                {
                    progress?.Report(
                        new TranslationProgress(
                            index,
                            sourceParts.Count));

                    string? translatedPart =
                        await TranslateJapaneseToChineseAsync(
                            sourceParts[index],
                            cancellationToken,
                            forceRefresh,
                            progress: null);

                    if (!string.IsNullOrWhiteSpace(
                            translatedPart))
                    {
                        translatedParts.Add(
                            translatedPart.Trim());
                    }

                }

                if (translatedParts.Count == 0)
                {
                    return null;
                }

                string combinedTranslation =
                    string.Join(
                        Environment.NewLine,
                        translatedParts);

                StoreTranslationInCache(
                    text,
                    combinedTranslation);

                return combinedTranslation;
            }

            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    "尚未填写有道翻译 AppKey 和 AppSecret。");
            }

            string salt =
                Guid.NewGuid()
                    .ToString("N");

            string currentTime =
                DateTimeOffset.UtcNow
                    .ToUnixTimeSeconds()
                    .ToString();

            string signatureInput =
                BuildSignatureInput(
                    text);

            string signSource =
                _config!.AppKey +
                signatureInput +
                salt +
                currentTime +
                _config.AppSecret;

            string signature =
                CreateSha256(
                    signSource);

            Dictionary<string, string> formValues =
                new()
                {
                    ["q"] =
                        text,

                    ["from"] =
                        "ja",

                    ["to"] =
                        "zh-CHS",

                    ["appKey"] =
                        _config.AppKey,

                    ["salt"] =
                        salt,

                    ["sign"] =
                        signature,

                    ["signType"] =
                        "v3",

                    ["curtime"] =
                        currentTime
                };

            using FormUrlEncodedContent content =
                new FormUrlEncodedContent(
                    formValues);

            using HttpResponseMessage response =
                await _httpClient.PostAsync(
                    ApiUrl,
                    content,
                    cancellationToken);

            string responseJson =
                await response.Content
                    .ReadAsStringAsync(
                        cancellationToken);

            response.EnsureSuccessStatusCode();

            using JsonDocument document =
                JsonDocument.Parse(
                    responseJson);

            JsonElement root =
                document.RootElement;

            string errorCode =
                root.TryGetProperty(
                    "errorCode",
                    out JsonElement errorCodeElement)
                    ? errorCodeElement.GetString() ??
                      string.Empty
                    : string.Empty;

            if (errorCode != "0")
            {
                throw new InvalidOperationException(
                    $"有道翻译失败，错误代码：{errorCode}");
            }

            if (!root.TryGetProperty(
                    "translation",
                    out JsonElement translationElement) ||
                translationElement.ValueKind !=
                    JsonValueKind.Array ||
                translationElement.GetArrayLength() == 0)
            {
                return null;
            }

            string? translatedText =
                translationElement[0]
                    .GetString();

            if (string.IsNullOrWhiteSpace(
                    translatedText))
            {
                return null;
            }

            translatedText =
                translatedText.Trim();

            StoreTranslationInCache(
                text,
                translatedText);

            return translatedText;
        }

        private void StoreTranslationInCache(
            string sourceText,
            string translatedText)
        {
            if (!_translationCache.ContainsKey(
                    sourceText) &&
                _translationCache.Count >=
                MaximumCachedTranslations)
            {
                /* 只保留本次运行中最近一批常用译文，避免长期占内存。 */
                _translationCache.Clear();
            }

            _translationCache[sourceText] =
                translatedText;
        }

        private static string NormalizeText(
            string text)
        {
            return text
                .Replace(
                    "\r\n",
                    "\n")
                .Replace(
                    '\r',
                    '\n')
                .Trim();
        }

        private static bool ContainsJapanese(
            string text)
        {
            foreach (char character in text)
            {
                if (character >= '\u3040' &&
                    character <= '\u30FF')
                {
                    return true;
                }

                if (character >= '\u31F0' &&
                    character <= '\u31FF')
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> SplitLongText(
            string text,
            int maximumLength)
        {
            int start = 0;

            while (start < text.Length)
            {
                int length =
                    Math.Min(
                        maximumLength,
                        text.Length - start);

                int end =
                    start + length;

                if (end < text.Length)
                {
                    int preferredEnd =
                        text.LastIndexOfAny(
                            new[]
                            {
                                '\n',
                                '。',
                                '！',
                                '？',
                                '!',
                                '?'
                            },
                            end - 1,
                            length);

                    if (preferredEnd >= start + 200)
                    {
                        end =
                            preferredEnd + 1;
                    }
                }

                string part =
                    text.Substring(
                        start,
                        end - start)
                        .Trim();

                if (!string.IsNullOrWhiteSpace(part))
                {
                    yield return part;
                }

                start = end;
            }
        }

        private static string BuildSignatureInput(
            string text)
        {
            if (text.Length <= 20)
            {
                return text;
            }

            string firstTen =
                text.Substring(
                    0,
                    10);

            string lastTen =
                text.Substring(
                    text.Length - 10,
                    10);

            return
                firstTen +
                text.Length +
                lastTen;
        }

        private static string CreateSha256(
            string value)
        {
            byte[] bytes =
                Encoding.UTF8.GetBytes(
                    value);

            byte[] hash =
                SHA256.HashData(
                    bytes);

            StringBuilder builder =
                new StringBuilder(
                    hash.Length * 2);

            foreach (byte item in hash)
            {
                builder.Append(
                    item.ToString("x2"));
            }

            return builder.ToString();
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

    public sealed class YoudaoConfig
    {
        public string AppKey { get; set; } =
            string.Empty;

        public string AppSecret { get; set; } =
            string.Empty;
    }

    public sealed record TranslationProgress(
        int CompletedParts,
        int TotalParts);
}
