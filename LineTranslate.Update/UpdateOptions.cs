namespace MultiChatManager2.Updates;

public sealed class UpdateOptions
{
    public required Uri ManifestUri { get; init; }

    public required string ProductId { get; init; }

    public required string Channel { get; init; }

    public required string CurrentVersion { get; init; }

    public required string InstallDirectory { get; init; }

    public required string MainExecutablePath { get; init; }

    public required string UpdaterExecutablePath { get; init; }

    public required string WorkDirectory { get; init; }

    public string? RsaPublicKeyPem { get; init; }

    public TimeSpan RequestTimeout { get; init; } =
        TimeSpan.FromSeconds(30);

    public int MaximumRetryCount { get; init; } = 4;

    public long MaximumPackageBytes { get; init; } =
        1024L * 1024L * 1024L;

    public bool RequireManifestSignature { get; init; } = true;

    public bool RequirePackageSignature { get; init; } = true;

    public bool AllowDowngrade { get; init; }

    public bool AllowPrerelease { get; init; }

    public void Validate()
    {
        if (!ManifestUri.IsAbsoluteUri)
        {
            throw new InvalidOperationException(
                "ManifestUri 必须是绝对地址。");
        }

        if (ManifestUri.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException(
                "ManifestUri 仅允许 HTTP 或 HTTPS。");
        }

        if (ManifestUri.Scheme != Uri.UriSchemeHttps &&
            !ManifestUri.IsLoopback)
        {
            throw new InvalidOperationException(
                "生产环境更新地址必须使用 HTTPS。");
        }

        if (string.IsNullOrWhiteSpace(ProductId))
        {
            throw new InvalidOperationException(
                "ProductId 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(Channel))
        {
            throw new InvalidOperationException(
                "Channel 不能为空。");
        }

        if (!SemanticVersion.TryParse(
                CurrentVersion,
                out _))
        {
            throw new InvalidOperationException(
                "CurrentVersion 格式无效。");
        }

        ValidateAbsoluteDirectory(
            InstallDirectory,
            nameof(InstallDirectory));

        ValidateAbsoluteDirectory(
            WorkDirectory,
            nameof(WorkDirectory));

        ValidateExecutable(
            MainExecutablePath,
            nameof(MainExecutablePath));

        ValidateExecutable(
            UpdaterExecutablePath,
            nameof(UpdaterExecutablePath));

        if (MaximumRetryCount < 0 ||
            MaximumRetryCount > 10)
        {
            throw new InvalidOperationException(
                "MaximumRetryCount 必须在 0 到 10 之间。");
        }

        if (MaximumPackageBytes < 1024 * 1024)
        {
            throw new InvalidOperationException(
                "MaximumPackageBytes 设置过小。");
        }

        if ((RequireManifestSignature ||
             RequirePackageSignature) &&
            string.IsNullOrWhiteSpace(
                RsaPublicKeyPem))
        {
            throw new InvalidOperationException(
                "启用签名验证时必须配置 RSA 公钥。");
        }
    }

    private static void ValidateAbsoluteDirectory(
        string path,
        string name)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(
                $"{name} 必须是绝对路径。");
        }
    }

    private static void ValidateExecutable(
        string path,
        string name)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) ||
            !string.Equals(
                Path.GetExtension(path),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{name} 必须是绝对 EXE 路径。");
        }
    }
}
