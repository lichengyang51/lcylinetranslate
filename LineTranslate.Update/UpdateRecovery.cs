using System.Text.Json;

namespace MultiChatManager2.Updates;

public static class UpdateRecovery
{
    public static UpdateRecoveryResult Inspect(
        string workDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                workDirectory) ||
            !Directory.Exists(
                workDirectory))
        {
            return UpdateRecoveryResult.None;
        }

        string? newestResult =
            Directory
                .EnumerateFiles(
                    workDirectory,
                    "apply-result.json",
                    SearchOption.AllDirectories)
                .Select(
                    path =>
                        new FileInfo(path))
                .OrderByDescending(
                    file =>
                        file.LastWriteTimeUtc)
                .FirstOrDefault()
                ?.FullName;

        if (newestResult is null)
        {
            return UpdateRecoveryResult.None;
        }

        try
        {
            string json =
                File.ReadAllText(
                    newestResult);

            ApplyUpdateResult? result =
                JsonSerializer.Deserialize
                    <ApplyUpdateResult>(
                        json,
                        UpdaterLaunchRequest.JsonOptions);

            if (result is null)
            {
                return new UpdateRecoveryResult(
                    true,
                    false,
                    null,
                    "更新结果文件无法解析。",
                    newestResult);
            }

            return new UpdateRecoveryResult(
                true,
                result.Success,
                result.Version,
                result.Message,
                newestResult);
        }
        catch (Exception exception)
        {
            return new UpdateRecoveryResult(
                true,
                false,
                null,
                "读取更新结果失败：" +
                exception.Message,
                newestResult);
        }
    }
}

public sealed record UpdateRecoveryResult(
    bool HasResult,
    bool Success,
    string? Version,
    string Message,
    string? ResultFilePath)
{
    public static UpdateRecoveryResult None { get; } =
        new(
            false,
            false,
            null,
            string.Empty,
            null);
}

public sealed class ApplyUpdateResult
{
    public bool Success { get; init; }

    public string? Version { get; init; }

    public required string Message { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public bool RolledBack { get; init; }
}
