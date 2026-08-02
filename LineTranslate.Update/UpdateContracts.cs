using System.Text.Json.Serialization;

namespace MultiChatManager2.Updates;

public sealed class UpdateManifestEnvelope
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("payload")]
    public required UpdateManifest Payload { get; init; }

    [JsonPropertyName("signature")]
    public string? Signature { get; init; }
}

public sealed class UpdateManifest
{
    [JsonPropertyName("productId")]
    public required string ProductId { get; init; }

    [JsonPropertyName("channel")]
    public required string Channel { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("minimumSupportedVersion")]
    public string? MinimumSupportedVersion { get; init; }

    [JsonPropertyName("publishedAtUtc")]
    public required DateTimeOffset PublishedAtUtc { get; init; }

    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; init; }

    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; init; }

    [JsonPropertyName("package")]
    public required UpdatePackage Package { get; init; }
}

public sealed class UpdatePackage
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("signature")]
    public string? Signature { get; init; }
}

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    bool IsMandatory,
    SemanticVersion CurrentVersion,
    SemanticVersion LatestVersion,
    UpdateManifest? Manifest,
    string Message)
{
    public static UpdateCheckResult NoUpdate(
        SemanticVersion currentVersion,
        string message) =>
        new(
            false,
            false,
            currentVersion,
            currentVersion,
            null,
            message);
}

public sealed record PreparedUpdate(
    UpdateManifest Manifest,
    string PackagePath,
    string SessionDirectory);

public enum UpdateStage
{
    Checking,
    Downloading,
    Verifying,
    Preparing,
    Ready,
    Launching,
    Completed
}

public sealed record UpdateProgressInfo(
    UpdateStage Stage,
    string Message,
    long BytesReceived = 0,
    long? TotalBytes = null)
{
    public double? Percentage =>
        TotalBytes is > 0
            ? Math.Clamp(
                BytesReceived * 100d /
                TotalBytes.Value,
                0d,
                100d)
            : null;
}
