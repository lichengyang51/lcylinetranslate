using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MultiChatManager2.Updates;

public sealed class UpdateClient :
    IDisposable
{
    private readonly UpdateOptions _options;
    private readonly HttpClient _httpClient;
    private readonly CryptographicVerifier _verifier;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public UpdateClient(
        UpdateOptions options,
        HttpMessageHandler? handler = null)
    {
        _options = options;
        _options.Validate();

        _httpClient =
            handler is null
                ? new HttpClient(
                    new SocketsHttpHandler
                    {
                        AutomaticDecompression =
                            DecompressionMethods.All,
                        PooledConnectionLifetime =
                            TimeSpan.FromMinutes(5),
                        ConnectTimeout =
                            TimeSpan.FromSeconds(15),
                        MaxConnectionsPerServer = 4
                    })
                : new HttpClient(
                    handler,
                    disposeHandler: true);

        _httpClient.Timeout =
            Timeout.InfiniteTimeSpan;

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"{_options.ProductId}/{_options.CurrentVersion}");

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        _verifier =
            new CryptographicVerifier(
                _options.RsaPublicKeyPem);

        _jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false
            };
    }

    public async Task<UpdateCheckResult> CheckAsync(
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        progress?.Report(
            new UpdateProgressInfo(
                UpdateStage.Checking,
                "正在检查更新……"));

        SemanticVersion currentVersion =
            SemanticVersion.Parse(
                _options.CurrentVersion);

        using HttpRequestMessage request =
            new(
                HttpMethod.Get,
                BuildManifestUri());

        using HttpResponseMessage response =
            await SendWithRetryAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpExceptionAsync(
                response,
                "获取更新清单失败",
                cancellationToken);
        }

        long? length =
            response.Content.Headers.ContentLength;

        if (length is > 1024 * 1024)
        {
            throw new InvalidDataException(
                "更新清单体积异常。");
        }

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        UpdateManifestEnvelope? envelope =
            await JsonSerializer.DeserializeAsync
                <UpdateManifestEnvelope>(
                    stream,
                    _jsonOptions,
                    cancellationToken);

        if (envelope is null)
        {
            throw new InvalidDataException(
                "更新清单为空。");
        }

        ValidateManifest(
            envelope);

        _verifier.VerifyManifestSignature(
            envelope,
            _options.RequireManifestSignature);

        UpdateManifest manifest =
            envelope.Payload;

        SemanticVersion latestVersion =
            SemanticVersion.Parse(
                manifest.Version);

        if (latestVersion.IsPrerelease &&
            !_options.AllowPrerelease)
        {
            return UpdateCheckResult.NoUpdate(
                currentVersion,
                "当前更新通道不接收预发布版本。");
        }

        int comparison =
            latestVersion.CompareTo(
                currentVersion);

        if (comparison == 0)
        {
            return UpdateCheckResult.NoUpdate(
                currentVersion,
                "当前已是最新版本。");
        }

        if (comparison < 0 &&
            !_options.AllowDowngrade)
        {
            return UpdateCheckResult.NoUpdate(
                currentVersion,
                "服务器版本低于当前版本，已忽略。");
        }

        bool mandatory =
            manifest.Mandatory;

        if (!string.IsNullOrWhiteSpace(
                manifest.MinimumSupportedVersion))
        {
            SemanticVersion minimum =
                SemanticVersion.Parse(
                    manifest.MinimumSupportedVersion);

            mandatory =
                mandatory ||
                currentVersion < minimum;
        }

        return new UpdateCheckResult(
            true,
            mandatory,
            currentVersion,
            latestVersion,
            manifest,
            mandatory
                ? "发现必须安装的新版本。"
                : "发现新版本。");
    }

    public async Task<PreparedUpdate> DownloadAndPrepareAsync(
        UpdateManifest manifest,
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        ValidateManifestPayload(
            manifest);

        string sessionDirectory =
            CreateSessionDirectory(
                manifest.Version);

        string packagePath =
            Path.Combine(
                sessionDirectory,
                "package.zip");

        string partialPath =
            packagePath + ".partial";

        try
        {
            await DownloadPackageAsync(
                manifest.Package,
                partialPath,
                progress,
                cancellationToken);

            progress?.Report(
                new UpdateProgressInfo(
                    UpdateStage.Verifying,
                    "正在验证更新包……"));

            await _verifier.VerifyPackageAsync(
                partialPath,
                manifest.Package,
                _options.RequirePackageSignature,
                cancellationToken);

            File.Move(
                partialPath,
                packagePath,
                overwrite: true);

            string manifestPath =
                Path.Combine(
                    sessionDirectory,
                    "manifest.json");

            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }),
                cancellationToken);

            progress?.Report(
                new UpdateProgressInfo(
                    UpdateStage.Ready,
                    "更新包已准备完成。"));

            return new PreparedUpdate(
                manifest,
                packagePath,
                sessionDirectory);
        }
        catch
        {
            SafeDelete(
                partialPath);

            throw;
        }
    }

    private async Task DownloadPackageAsync(
        UpdatePackage package,
        string partialPath,
        IProgress<UpdateProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        Uri packageUri =
            ResolvePackageUri(
                package.Url);

        // 更新包文件可能被 CDN 缓存。每次下载使用唯一查询参数，确保
        // 校验的是当前清单指向的原始 ZIP，而不是中间节点的旧响应。
        Uri downloadUri =
            AddDownloadCacheBuster(
                packageUri);

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                partialPath)!);

        long existingLength =
            File.Exists(partialPath)
                ? new FileInfo(partialPath).Length
                : 0;

        if (existingLength >= package.Size)
        {
            SafeDelete(
                partialPath);

            existingLength = 0;
        }

        for (int attempt = 0;
             attempt <= _options.MaximumRetryCount;
             attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                SafeDelete(partialPath);
                existingLength = 0;

                using HttpRequestMessage request =
                    new(
                        HttpMethod.Get,
                        downloadUri);

                // ZIP 必须按原始字节下载；避免代理或服务器对二进制更新包
                // 进行内容压缩/转换后导致 SHA-256 与发布清单不一致。
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "application/octet-stream"));
                request.Headers.AcceptEncoding.Clear();
                request.Headers.AcceptEncoding.Add(
                    new StringWithQualityHeaderValue(
                        "identity"));



                using HttpResponseMessage response =
                    await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                if (existingLength > 0 &&
                    response.StatusCode !=
                        HttpStatusCode.PartialContent)
                {
                    throw await CreateHttpExceptionAsync(
                        response,
                        "恢复下载失败",
                        cancellationToken);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateHttpExceptionAsync(
                        response,
                        "下载更新包失败",
                        cancellationToken);
                }

                long? responseLength =
                    response.Content.Headers.ContentLength;

                long totalBytes =
                    existingLength +
                    (responseLength ?? 0);

                if (package.Size > 0)
                {
                    totalBytes =
                        package.Size;
                }

                if (totalBytes >
                    _options.MaximumPackageBytes)
                {
                    throw new InvalidDataException(
                        "更新包超过允许的最大体积。");
                }

                await using Stream source =
                    await response.Content.ReadAsStreamAsync(
                        cancellationToken);

                await using FileStream destination =
                    new(
                        partialPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024,
                        FileOptions.Asynchronous |
                        FileOptions.SequentialScan |
                        FileOptions.WriteThrough);

                byte[] buffer =
                    new byte[1024 * 1024];

                long received =
                    existingLength;

                while (true)
                {
                    int count =
                        await source.ReadAsync(
                            buffer,
                            cancellationToken);

                    if (count == 0)
                    {
                        break;
                    }

                    received += count;

                    if (received >
                        _options.MaximumPackageBytes)
                    {
                        throw new InvalidDataException(
                            "下载数据超过允许的最大体积。");
                    }

                    await destination.WriteAsync(
                        buffer.AsMemory(
                            0,
                            count),
                        cancellationToken);

                    progress?.Report(
                        new UpdateProgressInfo(
                            UpdateStage.Downloading,
                            "正在下载更新包……",
                            received,
                            totalBytes > 0
                                ? totalBytes
                                : null));
                }

                await destination.FlushAsync(
                    cancellationToken);

                if (package.Size > 0 &&
                    received != package.Size)
                {
                    existingLength =
                        received;

                    throw new IOException(
                        $"更新包大小不完整，预期 {package.Size}，实际 {received}。");
                }

                return;
            }
            catch (Exception exception)
                when (IsTransient(exception) &&
                      attempt <
                      _options.MaximumRetryCount)
            {
                existingLength =
                    File.Exists(partialPath)
                        ? new FileInfo(partialPath).Length
                        : 0;

                TimeSpan delay =
                    CalculateRetryDelay(
                        attempt);

                await Task.Delay(
                    delay,
                    cancellationToken);
            }
        }

        throw new IOException(
            "更新包下载失败，已达到最大重试次数。");
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage originalRequest,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0;
             attempt <= _options.MaximumRetryCount;
             attempt++)
        {
            using CancellationTokenSource timeoutSource =
                new(
                    _options.RequestTimeout);

            using CancellationTokenSource linkedSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token);

            HttpRequestMessage request =
                await CloneRequestAsync(
                    originalRequest,
                    cancellationToken);

            try
            {
                HttpResponseMessage response =
                    await _httpClient.SendAsync(
                        request,
                        completionOption,
                        linkedSource.Token);

                if (!IsTransientStatus(
                        response.StatusCode) ||
                    attempt ==
                    _options.MaximumRetryCount)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (Exception exception)
                when (IsTransient(exception) &&
                      attempt <
                      _options.MaximumRetryCount)
            {
            }

            await Task.Delay(
                CalculateRetryDelay(attempt),
                cancellationToken);
        }

        throw new HttpRequestException(
            "更新服务器请求失败。");
    }

    private Uri BuildManifestUri()
    {
        string separator =
            string.IsNullOrWhiteSpace(
                _options.ManifestUri.Query)
                ? "?"
                : "&";

        return new Uri(
            _options.ManifestUri +
            separator +
            "productId=" +
            Uri.EscapeDataString(
                _options.ProductId) +
            "&channel=" +
            Uri.EscapeDataString(
                _options.Channel) +
            "&currentVersion=" +
            Uri.EscapeDataString(
                _options.CurrentVersion));
    }

    private Uri ResolvePackageUri(
        string value)
    {
        if (!Uri.TryCreate(
                value,
                UriKind.RelativeOrAbsolute,
                out Uri? uri))
        {
            throw new InvalidDataException(
                "更新包地址无效。");
        }

        Uri absolute =
            uri.IsAbsoluteUri
                ? uri
                : new Uri(
                    _options.ManifestUri,
                    uri);

        if (absolute.Scheme !=
                Uri.UriSchemeHttps &&
            !absolute.IsLoopback)
        {
            throw new UpdateSecurityException(
                "更新包地址必须使用 HTTPS。");
        }

        return absolute;
    }

    private static Uri AddDownloadCacheBuster(
        Uri uri)
    {
        string separator =
            string.IsNullOrWhiteSpace(uri.Query)
                ? "?"
                : "&";

        return new Uri(
            uri +
            separator +
            "downloadId=" +
            Guid.NewGuid().ToString("N"));
    }

    private void ValidateManifest(
        UpdateManifestEnvelope envelope)
    {
        if (envelope.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                "不支持的更新清单版本。");
        }

        ValidateManifestPayload(
            envelope.Payload);
    }

    private void ValidateManifestPayload(
        UpdateManifest manifest)
    {
        if (!string.Equals(
                manifest.ProductId,
                _options.ProductId,
                StringComparison.Ordinal))
        {
            throw new UpdateSecurityException(
                "更新清单 ProductId 不匹配。");
        }

        if (!string.Equals(
                manifest.Channel,
                _options.Channel,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException(
                "更新清单 Channel 不匹配。");
        }

        _ =
            SemanticVersion.Parse(
                manifest.Version);

        if (!string.IsNullOrWhiteSpace(
                manifest.MinimumSupportedVersion))
        {
            _ =
                SemanticVersion.Parse(
                    manifest.MinimumSupportedVersion);
        }

        if (manifest.PublishedAtUtc >
            DateTimeOffset.UtcNow.AddHours(24))
        {
            throw new InvalidDataException(
                "更新发布时间异常。");
        }

        if (manifest.Package.Size <= 0 ||
            manifest.Package.Size >
                _options.MaximumPackageBytes)
        {
            throw new InvalidDataException(
                "更新包体积无效。");
        }

        if (string.IsNullOrWhiteSpace(
                manifest.Package.Sha256))
        {
            throw new InvalidDataException(
                "更新包缺少 SHA-256。");
        }

        _ =
            ResolvePackageUri(
                manifest.Package.Url);
    }

    private string CreateSessionDirectory(
        string version)
    {
        Directory.CreateDirectory(
            _options.WorkDirectory);

        string safeVersion =
            string.Concat(
                version.Select(
                    character =>
                        Path.GetInvalidFileNameChars()
                            .Contains(character)
                            ? '_'
                            : character));

        string sessionDirectory =
            Path.Combine(
                _options.WorkDirectory,
                safeVersion +
                "-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            sessionDirectory);

        return sessionDirectory;
    }

    private static TimeSpan CalculateRetryDelay(
        int attempt)
    {
        double milliseconds =
            Math.Min(
                15000,
                500 *
                Math.Pow(
                    2,
                    attempt));

        milliseconds +=
            Random.Shared.Next(
                100,
                500);

        return TimeSpan.FromMilliseconds(
            milliseconds);
    }

    private static bool IsTransient(
        Exception exception) =>
        exception is HttpRequestException or
                     IOException or
                     TaskCanceledException;

    private static bool IsTransientStatus(
        HttpStatusCode statusCode) =>
        statusCode is
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout ||
        (int)statusCode >= 500;

    private static async Task<HttpRequestMessage>
        CloneRequestAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
    {
        HttpRequestMessage clone =
            new(
                request.Method,
                request.RequestUri);

        foreach (KeyValuePair<string, IEnumerable<string>>
                 header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(
                header.Key,
                header.Value);
        }

        if (request.Content is not null)
        {
            byte[] content =
                await request.Content
                    .ReadAsByteArrayAsync(
                        cancellationToken);

            clone.Content =
                new ByteArrayContent(
                    content);

            foreach (KeyValuePair<string, IEnumerable<string>>
                     header in request.Content.Headers)
            {
                clone.Content.Headers
                    .TryAddWithoutValidation(
                        header.Key,
                        header.Value);
            }
        }

        return clone;
    }

    private static async Task<Exception>
        CreateHttpExceptionAsync(
            HttpResponseMessage response,
            string prefix,
            CancellationToken cancellationToken)
    {
        string body =
            await response.Content
                .ReadAsStringAsync(
                    cancellationToken);

        if (body.Length > 1000)
        {
            body =
                body[..1000];
        }

        return new HttpRequestException(
            $"{prefix}：HTTP {(int)response.StatusCode} {response.ReasonPhrase}。{body}");
    }

    private static void SafeDelete(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }
}
