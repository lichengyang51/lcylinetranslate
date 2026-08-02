using System.Diagnostics;
using System.Text.Json;

namespace MultiChatManager2.Updates;

public sealed class UpdateCoordinator :
    IDisposable
{
    private readonly UpdateOptions _options;
    private readonly UpdateClient _client;
    private readonly SemaphoreSlim _operationLock =
        new(
            1,
            1);

    private bool _disposed;

    public UpdateCoordinator(
        UpdateOptions options,
        HttpMessageHandler? handler = null)
    {
        _options = options;
        _options.Validate();

        _client =
            new UpdateClient(
                options,
                handler);
    }

    public async Task<UpdateCheckResult> CheckAsync(
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _operationLock.WaitAsync(
            cancellationToken);

        try
        {
            return await _client.CheckAsync(
                progress,
                cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<PreparedUpdate> PrepareAsync(
        UpdateManifest manifest,
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _operationLock.WaitAsync(
            cancellationToken);

        try
        {
            return await _client
                .DownloadAndPrepareAsync(
                    manifest,
                    progress,
                    cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<PreparedUpdate?> CheckAndPrepareAsync(
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        UpdateCheckResult check =
            await CheckAsync(
                progress,
                cancellationToken);

        if (!check.IsUpdateAvailable ||
            check.Manifest is null)
        {
            return null;
        }

        return await PrepareAsync(
            check.Manifest,
            progress,
            cancellationToken);
    }

    public void LaunchUpdaterAndExit(
        PreparedUpdate preparedUpdate,
        string[]? restartArguments = null,
        Action? beforeExit = null)
    {
        ThrowIfDisposed();

        if (!File.Exists(
                preparedUpdate.PackagePath))
        {
            throw new FileNotFoundException(
                "更新包不存在。",
                preparedUpdate.PackagePath);
        }

        if (!File.Exists(
                _options.UpdaterExecutablePath))
        {
            throw new FileNotFoundException(
                "独立更新器不存在。",
                _options.UpdaterExecutablePath);
        }

        UpdaterLaunchRequest request =
            new()
            {
                ParentProcessId =
                    Environment.ProcessId,

                ProductId =
                    _options.ProductId,

                TargetVersion =
                    preparedUpdate.Manifest.Version,

                InstallDirectory =
                    Path.GetFullPath(
                        _options.InstallDirectory),

                PackagePath =
                    Path.GetFullPath(
                        preparedUpdate.PackagePath),

                MainExecutablePath =
                    Path.GetFullPath(
                        _options.MainExecutablePath),

                RestartArguments =
                    restartArguments ??
                    Array.Empty<string>(),

                SessionDirectory =
                    Path.GetFullPath(
                        preparedUpdate.SessionDirectory)
            };

        string requestPath =
            Path.Combine(
                preparedUpdate.SessionDirectory,
                "apply-request.json");

        File.WriteAllText(
            requestPath,
            JsonSerializer.Serialize(
                request,
                UpdaterLaunchRequest.JsonOptions));

        ProcessStartInfo startInfo =
            new()
            {
                FileName =
                    _options.UpdaterExecutablePath,

                // 更新时需要覆盖安装目录中的 EXE 和 DLL；当程序安装在
                // 受保护目录时，独立更新器必须通过 UAC 获取写入权限。
                UseShellExecute = true,

                Verb = "runas",

                WorkingDirectory =
                    Path.GetDirectoryName(
                        _options.UpdaterExecutablePath)!,

                Arguments =
                    $"--request \"{requestPath}\""
            };

        Process? updater =
            Process.Start(
                startInfo);

        if (updater is null)
        {
            throw new InvalidOperationException(
                "无法启动独立更新器。");
        }

        beforeExit?.Invoke();

        Environment.Exit(0);
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
        _client.Dispose();
        _operationLock.Dispose();
    }
}

public sealed class UpdaterLaunchRequest
{
    public int ParentProcessId { get; init; }

    public required string ProductId { get; init; }

    public required string TargetVersion { get; init; }

    public required string InstallDirectory { get; init; }

    public required string PackagePath { get; init; }

    public required string MainExecutablePath { get; init; }

    public required string SessionDirectory { get; init; }

    public string[] RestartArguments { get; init; } =
        Array.Empty<string>();

    public static JsonSerializerOptions JsonOptions { get; } =
        new()
        {
            PropertyNameCaseInsensitive = false,
            WriteIndented = true
        };
}
