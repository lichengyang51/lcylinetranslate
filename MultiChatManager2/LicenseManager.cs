using Microsoft.Win32;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MultiChatManager2
{
    public sealed class LicenseManager : IDisposable
    {
        private readonly HttpClient _httpClient;

        private readonly string _licenseFilePath;

        private readonly string _functionUrl;

        private readonly string _publishableKey;

        public LicenseManager(
            string dataFolder,
            string functionUrl,
            string publishableKey)
        {
            Directory.CreateDirectory(
                dataFolder);

            _licenseFilePath =
                Path.Combine(
                    dataFolder,
                    "license.json");

            _functionUrl =
                functionUrl.Trim();

            _publishableKey =
                publishableKey.Trim();

            _httpClient =
                new HttpClient
                {
                    Timeout =
                        TimeSpan.FromSeconds(20)
                };
        }

        public string DeviceId =>
            CreateDeviceId();

        public string? SavedLicenseKey =>
            LoadSavedLicenseKey();

        public bool HasSavedLicense =>
            !string.IsNullOrWhiteSpace(
                SavedLicenseKey);

        public async Task<LicenseResult> VerifySavedLicenseAsync(
            CancellationToken cancellationToken =
                default)
        {
            string? licenseKey =
                LoadSavedLicenseKey();

            if (string.IsNullOrWhiteSpace(
                    licenseKey))
            {
                return LicenseResult.Failed(
                    "尚未输入激活码。",
                    "LICENSE_NOT_SAVED");
            }

            return await VerifyLicenseAsync(
                licenseKey,
                true,
                cancellationToken);
        }

        public async Task<LicenseResult> ActivateAsync(
            string licenseKey,
            CancellationToken cancellationToken =
                default)
        {
            return await VerifyLicenseAsync(
                licenseKey,
                true,
                cancellationToken);
        }

        public void ClearSavedLicense()
        {
            try
            {
                if (File.Exists(
                        _licenseFilePath))
                {
                    File.Delete(
                        _licenseFilePath);
                }
            }
            catch
            {
            }
        }

        private async Task<LicenseResult> VerifyLicenseAsync(
            string licenseKey,
            bool saveWhenSuccessful,
            CancellationToken cancellationToken)
        {
            string normalizedLicenseKey =
                licenseKey.Trim();

            if (string.IsNullOrWhiteSpace(
                    normalizedLicenseKey))
            {
                return LicenseResult.Failed(
                    "请输入激活码。",
                    "LICENSE_EMPTY");
            }

            if (string.IsNullOrWhiteSpace(
                    _functionUrl) ||
                string.IsNullOrWhiteSpace(
                    _publishableKey))
            {
                return LicenseResult.Failed(
                    "授权服务器尚未配置。",
                    "SERVER_NOT_CONFIGURED");
            }

            try
            {
                VerifyLicenseRequest requestData =
                    new VerifyLicenseRequest
                    {
                        LicenseKey =
                            normalizedLicenseKey,

                        DeviceId =
                            DeviceId
                    };

                string requestJson =
                    JsonSerializer.Serialize(
                        requestData);

                using HttpRequestMessage request =
                    new HttpRequestMessage(
                        HttpMethod.Post,
                        _functionUrl);

                request.Headers.TryAddWithoutValidation(
                    "apikey",
                    _publishableKey);

                request.Headers.TryAddWithoutValidation(
                    "Authorization",
                    "Bearer " +
                    _publishableKey);

                request.Content =
                    new StringContent(
                        requestJson,
                        Encoding.UTF8,
                        "application/json");

                using HttpResponseMessage response =
                    await _httpClient.SendAsync(
                        request,
                        cancellationToken);

                string responseJson =
                    await response.Content
                        .ReadAsStringAsync(
                            cancellationToken);

                LicenseServerResponse? serverResponse =
                    JsonSerializer.Deserialize<LicenseServerResponse>(
                        responseJson,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        });

                if (serverResponse == null)
                {
                    return LicenseResult.Failed(
                        "授权服务器返回了无法识别的内容。",
                        "INVALID_RESPONSE");
                }

                if (!serverResponse.Success)
                {
                    return LicenseResult.Failed(
                        string.IsNullOrWhiteSpace(
                            serverResponse.Message)
                            ? "激活码验证失败。"
                            : serverResponse.Message,

                        serverResponse.Code);
                }

                if (saveWhenSuccessful)
                {
                    SaveLicense(
                        normalizedLicenseKey,
                        serverResponse.ExpireAt);
                }

                return LicenseResult.Succeeded(
                    serverResponse.Message,
                    serverResponse.ExpireAt,
                    serverResponse.Permanent);
            }
            catch (TaskCanceledException)
            {
                return LicenseResult.Failed(
                    "连接授权服务器超时，请检查网络。",
                    "REQUEST_TIMEOUT");
            }
            catch (HttpRequestException exception)
            {
                return LicenseResult.Failed(
                    "无法连接授权服务器：" +
                    exception.Message,
                    "NETWORK_ERROR");
            }
            catch (JsonException)
            {
                return LicenseResult.Failed(
                    "授权服务器返回格式错误。",
                    "INVALID_JSON");
            }
            catch (Exception exception)
            {
                return LicenseResult.Failed(
                    "授权验证失败：" +
                    exception.Message,
                    "UNKNOWN_ERROR");
            }
        }

        private void SaveLicense(
            string licenseKey,
            string? expireAt)
        {
            SavedLicenseData data =
                new SavedLicenseData
                {
                    LicenseKey =
                        licenseKey,

                    DeviceId =
                        DeviceId,

                    ExpireAt =
                        expireAt,

                    SavedAt =
                        DateTimeOffset.UtcNow
                            .ToString("O")
                };

            string json =
                JsonSerializer.Serialize(
                    data,
                    new JsonSerializerOptions
                    {
                        WriteIndented =
                            true
                    });

            File.WriteAllText(
                _licenseFilePath,
                json,
                Encoding.UTF8);
        }

        private string? LoadSavedLicenseKey()
        {
            try
            {
                if (!File.Exists(
                        _licenseFilePath))
                {
                    return null;
                }

                string json =
                    File.ReadAllText(
                        _licenseFilePath,
                        Encoding.UTF8);

                SavedLicenseData? data =
                    JsonSerializer.Deserialize<SavedLicenseData>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        });

                if (data == null ||
                    string.IsNullOrWhiteSpace(
                        data.LicenseKey))
                {
                    return null;
                }

                if (!string.Equals(
                        data.DeviceId,
                        DeviceId,
                        StringComparison.Ordinal))
                {
                    return null;
                }

                return data.LicenseKey.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string CreateDeviceId()
        {
            string machineGuid =
                ReadMachineGuid();

            string source =
                machineGuid +
                "|" +
                Environment.MachineName +
                "|" +
                Environment.OSVersion.Platform;

            byte[] bytes =
                Encoding.UTF8.GetBytes(
                    source);

            byte[] hash =
                SHA256.HashData(
                    bytes);

            return Convert.ToHexString(
                hash);
        }

        private static string ReadMachineGuid()
        {
            try
            {
                using RegistryKey? key =
                    Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Cryptography");

                object? value =
                    key?.GetValue(
                        "MachineGuid");

                if (value is string machineGuid &&
                    !string.IsNullOrWhiteSpace(
                        machineGuid))
                {
                    return machineGuid.Trim();
                }
            }
            catch
            {
            }

            return Environment.MachineName;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

    public sealed class LicenseResult
    {
        public bool Success { get; private set; }

        public string Message { get; private set; } =
            string.Empty;

        public string? Code { get; private set; }

        public string? ExpireAt { get; private set; }

        public bool Permanent { get; private set; }

        public static LicenseResult Succeeded(
            string? message,
            string? expireAt,
            bool permanent)
        {
            return new LicenseResult
            {
                Success =
                    true,

                Message =
                    string.IsNullOrWhiteSpace(
                        message)
                        ? "授权验证成功。"
                        : message,

                ExpireAt =
                    expireAt,

                Permanent =
                    permanent
            };
        }

        public static LicenseResult Failed(
            string message,
            string? code)
        {
            return new LicenseResult
            {
                Success =
                    false,

                Message =
                    message,

                Code =
                    code
            };
        }
    }

    public sealed class VerifyLicenseRequest
    {
        [JsonPropertyName("licenseKey")]
        public string LicenseKey { get; set; } =
            string.Empty;

        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; } =
            string.Empty;
    }

    public sealed class LicenseServerResponse
    {
        public bool Success { get; set; }

        public string? Code { get; set; }

        public string? Message { get; set; }

        public bool DeviceBound { get; set; }

        public bool Permanent { get; set; }

        public string? ExpireAt { get; set; }

        public string? ServerTime { get; set; }
    }

    public sealed class SavedLicenseData
    {
        public string LicenseKey { get; set; } =
            string.Empty;

        public string DeviceId { get; set; } =
            string.Empty;

        public string? ExpireAt { get; set; }

        public string SavedAt { get; set; } =
            string.Empty;
    }
}