using System.Text;
using System.Text.Json;

namespace MultiChatManager2.Updates;

internal static class CanonicalJson
{
    public static byte[] SerializePayload(
        UpdateManifest payload)
    {
        using MemoryStream stream =
            new();

        using Utf8JsonWriter writer =
            new(
                stream,
                new JsonWriterOptions
                {
                    Indented = false,
                    SkipValidation = false
                });

        writer.WriteStartObject();

        writer.WriteString(
            "channel",
            payload.Channel);

        writer.WriteBoolean(
            "mandatory",
            payload.Mandatory);

        if (payload.MinimumSupportedVersion is null)
        {
            writer.WriteNull(
                "minimumSupportedVersion");
        }
        else
        {
            writer.WriteString(
                "minimumSupportedVersion",
                payload.MinimumSupportedVersion);
        }

        writer.WriteStartObject(
            "package");

        writer.WriteString(
            "sha256",
            payload.Package.Sha256);

        if (payload.Package.Signature is null)
        {
            writer.WriteNull(
                "signature");
        }
        else
        {
            writer.WriteString(
                "signature",
                payload.Package.Signature);
        }

        writer.WriteNumber(
            "size",
            payload.Package.Size);

        writer.WriteString(
            "url",
            payload.Package.Url);

        writer.WriteEndObject();

        writer.WriteString(
            "productId",
            payload.ProductId);

        writer.WriteString(
            "publishedAtUtc",
            payload.PublishedAtUtc
                .ToUniversalTime()
                .ToString("O"));

        if (payload.ReleaseNotes is null)
        {
            writer.WriteNull(
                "releaseNotes");
        }
        else
        {
            writer.WriteString(
                "releaseNotes",
                payload.ReleaseNotes);
        }

        writer.WriteString(
            "version",
            payload.Version);

        writer.WriteEndObject();
        writer.Flush();

        return stream.ToArray();
    }
}
