using MultiChatManager2.Updates;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace LineTranslate.Updater;

internal static class Program
{
    private const int ParentExitTimeoutSeconds = 60;
    private const int RestartVerificationSeconds = 15;

    [STAThread]
    private static async Task<int> Main(
        string[] args)
    {
        string? requestPath =
            ParseRequestPath(
                args);

        if (requestPath is null)
        {
            return 2;
        }

        UpdaterLaunchRequest? request = null;

        try
        {
            request =
                await LoadRequestAsync(
                    requestPath);

            ValidateRequest(
                request);

            await WaitForParentExitAsync(
                request.ParentProcessId);

            string stagingDirectory =
                Path.Combine(
                    request.SessionDirectory,
                    "staging");

            string backupDirectory =
                Path.Combine(
                    request.SessionDirectory,
                    "backup");

            RecreateDirectory(
                stagingDirectory);

            RecreateDirectory(
                backupDirectory);

            ExtractPackageSecurely(
                request.PackagePath,
                stagingDirectory);

            ValidateStagingDirectory(
                stagingDirectory,
                request.MainExecutablePath,
                request.InstallDirectory);

            await ApplyWithRollbackAsync(
                request,
                stagingDirectory,
                backupDirectory);

            Process restartedProcess =
                RestartApplication(
                    request);

            await VerifyRestartAsync(
                restartedProcess);

            await WriteResultAsync(
                request,
                new ApplyUpdateResult
                {
                    Success = true,
                    Version =
                        request.TargetVersion,
                    Message =
                        "更新安装成功。",
                    CompletedAtUtc =
                        DateTimeOffset.UtcNow,
                    RolledBack = false
                });

            CleanupSuccessfulSession(
                request.SessionDirectory,
                request.PackagePath,
                stagingDirectory,
                backupDirectory);

            return 0;
        }
        catch (Exception exception)
        {
            if (request is not null)
            {
                await WriteResultBestEffortAsync(
                    request,
                    new ApplyUpdateResult
                    {
                        Success = false,
                        Version =
                            request.TargetVersion,
                        Message =
                            exception.ToString(),
                        CompletedAtUtc =
                            DateTimeOffset.UtcNow,
                        RolledBack =
                            Directory.Exists(
                                Path.Combine(
                                    request.SessionDirectory,
                                    "backup"))
                    });
            }

            return 1;
        }
    }

    private static string? ParseRequestPath(
        string[] args)
    {
        for (int index = 0;
             index < args.Length - 1;
             index++)
        {
            if (string.Equals(
                    args[index],
                    "--request",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(
                    args[index + 1]);
            }
        }

        return null;
    }

    private static async Task<UpdaterLaunchRequest>
        LoadRequestAsync(
            string requestPath)
    {
        if (!File.Exists(requestPath))
        {
            throw new FileNotFoundException(
                "更新请求文件不存在。",
                requestPath);
        }

        await using FileStream stream =
            new(
                requestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        UpdaterLaunchRequest? request =
            await JsonSerializer.DeserializeAsync
                <UpdaterLaunchRequest>(
                    stream,
                    UpdaterLaunchRequest.JsonOptions);

        return request ??
               throw new InvalidDataException(
                   "更新请求内容无效。");
    }

    private static void ValidateRequest(
        UpdaterLaunchRequest request)
    {
        if (request.ParentProcessId <= 0)
        {
            throw new InvalidDataException(
                "父进程 ID 无效。");
        }

        if (string.IsNullOrWhiteSpace(
                request.ProductId))
        {
            throw new InvalidDataException(
                "ProductId 不能为空。");
        }

        _ =
            SemanticVersion.Parse(
                request.TargetVersion);

        string installDirectory =
            EnsureAbsolutePath(
                request.InstallDirectory);

        string packagePath =
            EnsureAbsolutePath(
                request.PackagePath);

        string mainExecutablePath =
            EnsureAbsolutePath(
                request.MainExecutablePath);

        string sessionDirectory =
            EnsureAbsolutePath(
                request.SessionDirectory);

        EnsurePathInsideDirectory(
            mainExecutablePath,
            installDirectory,
            "主程序路径");

        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException(
                "更新包不存在。",
                packagePath);
        }

        if (!Directory.Exists(
                installDirectory))
        {
            throw new DirectoryNotFoundException(
                "安装目录不存在。");
        }

        Directory.CreateDirectory(
            sessionDirectory);
    }

    private static async Task WaitForParentExitAsync(
        int processId)
    {
        Process? process;

        try
        {
            process =
                Process.GetProcessById(
                    processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            using CancellationTokenSource timeout =
                new(
                    TimeSpan.FromSeconds(
                        ParentExitTimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(
                    timeout.Token);
                await Task.Delay(1000);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(
                        entireProcessTree: true);

                    await process.WaitForExitAsync();
                    await Task.Delay(1000);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "主程序未能在规定时间内退出。",
                        exception);
                }
            }
        }
    }

    private static void ExtractPackageSecurely(
        string packagePath,
        string stagingDirectory)
    {
        string stagingRoot =
            EnsureTrailingSeparator(
                Path.GetFullPath(
                    stagingDirectory));

        using ZipArchive archive =
            ZipFile.OpenRead(
                packagePath);

        if (archive.Entries.Count == 0)
        {
            throw new InvalidDataException(
                "更新包为空。");
        }

        foreach (ZipArchiveEntry entry
                 in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(
                    entry.FullName))
            {
                continue;
            }

            string normalizedEntryName =
                entry.FullName
                    .Replace(
                        '\\',
                        '/');

            if (normalizedEntryName.StartsWith(
                    "/",
                    StringComparison.Ordinal) ||
                normalizedEntryName.Contains(
                    "../",
                    StringComparison.Ordinal) ||
                Path.IsPathRooted(
                    normalizedEntryName))
            {
                throw new InvalidDataException(
                    "更新包包含不安全路径。");
            }

            string destinationPath =
                Path.GetFullPath(
                    Path.Combine(
                        stagingDirectory,
                        normalizedEntryName));

            if (!destinationPath.StartsWith(
                    stagingRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "更新包尝试写出暂存目录。");
            }

            if (normalizedEntryName.EndsWith(
                    "/",
                    StringComparison.Ordinal))
            {
                Directory.CreateDirectory(
                    destinationPath);

                continue;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    destinationPath)!);

            using Stream input =
                entry.Open();

            using FileStream output =
                new(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);

            input.CopyTo(output);
            output.Flush(
                flushToDisk: true);
        }
    }

    private static void ValidateStagingDirectory(
        string stagingDirectory,
        string mainExecutablePath,
        string installDirectory)
    {
        string relativeMainExecutable =
            Path.GetFileName(mainExecutablePath);

        string stagedMainExecutable =
            Path.Combine(
                stagingDirectory,
                relativeMainExecutable);


        if (!File.Exists(stagedMainExecutable))
        {
            string? detectedExecutable =
                Directory
                    .EnumerateFiles(
                        stagingDirectory,
                        "*.exe",
                        SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(path =>
                        !Path.GetFileName(path).Equals(
                            "LineTranslate.Updater.exe",
                            StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(path).StartsWith(
                            "unins",
                            StringComparison.OrdinalIgnoreCase));

            if (detectedExecutable is null)
            {
                throw new InvalidDataException(
                    "更新包中缺少主程序 EXE。");
            }

            stagedMainExecutable = detectedExecutable;
        }

        string[] forbidden =
        {
            "apply-request.json",
            "apply-result.json"
        };

        foreach (string fileName in forbidden)
        {
            if (Directory.EnumerateFiles(
                    stagingDirectory,
                    fileName,
                    SearchOption.AllDirectories)
                .Any())
            {
                throw new InvalidDataException(
                    $"更新包包含禁止文件：{fileName}");
            }
        }
    }

    private static async Task ApplyWithRollbackAsync(
        UpdaterLaunchRequest request,
        string stagingDirectory,
        string backupDirectory)
    {
        string installDirectory =
            Path.GetFullPath(
                request.InstallDirectory);

        string[] incomingFiles =
            Directory
                .EnumerateFiles(
                    stagingDirectory,
                    "*",
                    SearchOption.AllDirectories)
                .Where(file =>
                {
                    string relativePath =
                        Path.GetRelativePath(
                            stagingDirectory,
                            file);

                    bool isUpdaterFile =
                        relativePath.StartsWith(
                            "Updater" + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase);

                    bool isUserDataFile =
                        relativePath.Equals(
                            "Data",
                            StringComparison.OrdinalIgnoreCase) ||
                        relativePath.StartsWith(
                            "Data" + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase);

                    return !isUpdaterFile && !isUserDataFile;
                })
                .ToArray();

        List<FileOperation> operations =
            new();

        foreach (string stagedFile
                 in incomingFiles)
        {
            string relativePath =
                Path.GetRelativePath(
                    stagingDirectory,
                    stagedFile);

            string targetPath =
                Path.GetFullPath(
                    Path.Combine(
                        installDirectory,
                        relativePath));

            EnsurePathInsideDirectory(
                targetPath,
                installDirectory,
                "目标文件");

            string backupPath =
                Path.Combine(
                    backupDirectory,
                    relativePath);

            operations.Add(
                new FileOperation(
                    stagedFile,
                    targetPath,
                    backupPath,
                    File.Exists(targetPath)));
        }

        try
        {
            foreach (FileOperation operation
                     in operations)
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(
                        operation.TargetPath)!);

                if (operation.TargetExisted)
                {
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(
                            operation.BackupPath)!);

                    File.Copy(
                        operation.TargetPath,
                        operation.BackupPath,
                        overwrite: true);
                }

                ReplaceFile(
                    operation.SourcePath,
                    operation.TargetPath);
            }

            await File.WriteAllTextAsync(
                Path.Combine(
                    request.SessionDirectory,
                    "applied-files.json"),
                JsonSerializer.Serialize(
                    operations.Select(
                        operation =>
                            new
                            {
                                operation.TargetPath,
                                operation.TargetExisted
                            }),
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch
        {
            Rollback(
                operations);

            throw;
        }
    }

    private static void ReplaceFile(
        string sourcePath,
        string targetPath)
    {
        const int maximumAttempts = 20;
        Exception? lastException = null;

        for (int attempt = 1;
             attempt <= maximumAttempts;
             attempt++)
        {
            string temporaryPath =
                targetPath +
                ".update-" +
                Guid.NewGuid().ToString("N");

            try
            {
                if (File.Exists(targetPath))
                {
                    FileAttributes attributes =
                        File.GetAttributes(targetPath);

                    if ((attributes & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(
                            targetPath,
                            attributes &
                            ~FileAttributes.ReadOnly);
                    }
                }

                File.Copy(
                    sourcePath,
                    temporaryPath,
                    overwrite: false);

                using (FileStream stream =
                       new(
                           temporaryPath,
                           FileMode.Open,
                           FileAccess.ReadWrite,
                           FileShare.None))
                {
                    stream.Flush(
                        flushToDisk: true);
                }

                File.Move(
                    temporaryPath,
                    targetPath,
                    overwrite: true);

                return;
            }
            catch (UnauthorizedAccessException exception)
            {
                lastException = exception;
            }
            catch (IOException exception)
            {
                lastException = exception;
            }
            finally
            {
                SafeDelete(temporaryPath);
            }

            if (attempt < maximumAttempts)
            {
                Thread.Sleep(500);
            }
        }

        throw new IOException(
            $"无法替换文件：{targetPath}。请关闭所有程序后重试。",
            lastException);
    }

    private static void Rollback(
        IReadOnlyList<FileOperation> operations)
    {
        foreach (FileOperation operation
                 in operations.Reverse())
        {
            try
            {
                if (operation.TargetExisted)
                {
                    if (File.Exists(
                            operation.BackupPath))
                    {
                        File.Copy(
                            operation.BackupPath,
                            operation.TargetPath,
                            overwrite: true);
                    }
                }
                else if (File.Exists(
                             operation.TargetPath))
                {
                    File.Delete(
                        operation.TargetPath);
                }
            }
            catch
            {
            }
        }
    }

    private static Process RestartApplication(
        UpdaterLaunchRequest request)
    {
        ProcessStartInfo startInfo =
            new()
            {
                FileName =
                    request.MainExecutablePath,

                WorkingDirectory =
                    request.InstallDirectory,

                UseShellExecute = true
            };

        foreach (string argument
                 in request.RestartArguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        return Process.Start(
                   startInfo) ??
               throw new InvalidOperationException(
                   "更新完成后无法重新启动主程序。");
    }

    private static async Task VerifyRestartAsync(
        Process process)
    {
        await Task.Delay(
            TimeSpan.FromSeconds(
                RestartVerificationSeconds));

        if (process.HasExited &&
            process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"更新后的主程序启动失败，退出码：{process.ExitCode}。");
        }
    }

    private static async Task WriteResultAsync(
        UpdaterLaunchRequest request,
        ApplyUpdateResult result)
    {
        string resultPath =
            Path.Combine(
                request.SessionDirectory,
                "apply-result.json");

        await File.WriteAllTextAsync(
            resultPath,
            JsonSerializer.Serialize(
                result,
                UpdaterLaunchRequest.JsonOptions));
    }

    private static async Task WriteResultBestEffortAsync(
        UpdaterLaunchRequest request,
        ApplyUpdateResult result)
    {
        try
        {
            await WriteResultAsync(
                request,
                result);
        }
        catch
        {
        }
    }

    private static void CleanupSuccessfulSession(
        string sessionDirectory,
        string packagePath,
        string stagingDirectory,
        string backupDirectory)
    {
        SafeDelete(
            packagePath);

        SafeDeleteDirectory(
            stagingDirectory);

        SafeDeleteDirectory(
            backupDirectory);

        foreach (string partial in
                 Directory.EnumerateFiles(
                     sessionDirectory,
                     "*.partial",
                     SearchOption.TopDirectoryOnly))
        {
            SafeDelete(
                partial);
        }
    }

    private static string EnsureAbsolutePath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path))
        {
            throw new InvalidDataException(
                "请求中包含非绝对路径。");
        }

        return Path.GetFullPath(path);
    }

    private static void EnsurePathInsideDirectory(
        string path,
        string directory,
        string description)
    {
        string fullPath =
            Path.GetFullPath(path);

        string fullDirectory =
            EnsureTrailingSeparator(
                Path.GetFullPath(directory));

        if (!fullPath.StartsWith(
                fullDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{description}超出安装目录。");
        }
    }

    private static string EnsureTrailingSeparator(
        string path) =>
        path.EndsWith(
            Path.DirectorySeparatorChar)
            ? path
            : path +
              Path.DirectorySeparatorChar;

    private static void RecreateDirectory(
        string path)
    {
        SafeDeleteDirectory(
            path);

        Directory.CreateDirectory(
            path);
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

    private static void SafeDeleteDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed record FileOperation(
        string SourcePath,
        string TargetPath,
        string BackupPath,
        bool TargetExisted);
}
