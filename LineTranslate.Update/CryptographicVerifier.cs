using System.Security.Cryptography;
using System.Text;

namespace MultiChatManager2.Updates;

internal sealed class CryptographicVerifier
{
    private readonly string? _rsaPublicKeyPem;

    public CryptographicVerifier(
        string? rsaPublicKeyPem)
    {
        _rsaPublicKeyPem =
            rsaPublicKeyPem;
    }

    public void VerifyManifestSignature(
        UpdateManifestEnvelope envelope,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(
                envelope.Signature))
        {
            if (required)
            {
                throw new UpdateSecurityException(
                    "更新清单缺少数字签名。");
            }

            return;
        }

        byte[] content =
            CanonicalJson.SerializePayload(
                envelope.Payload);

        VerifyRsaSignature(
            content,
            envelope.Signature,
            "更新清单");
    }

    public async Task VerifyPackageAsync(
        string packagePath,
        UpdatePackage package,
        bool signatureRequired,
        CancellationToken cancellationToken)
    {
        string actualHash =
            await ComputeSha256Async(
                packagePath,
                cancellationToken);

        string expectedHash =
            NormalizeHex(
                package.Sha256);

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash),
                Convert.FromHexString(expectedHash)))
        {
            throw new UpdateSecurityException(
                "更新包 SHA-256 校验失败。" +
                $"期望：{expectedHash}；实际：{actualHash}。");
        }

        if (string.IsNullOrWhiteSpace(
                package.Signature))
        {
            if (signatureRequired)
            {
                throw new UpdateSecurityException(
                    "更新包缺少数字签名。");
            }

            return;
        }

        byte[] hashBytes =
            Convert.FromHexString(
                actualHash);

        VerifyRsaSignature(
            hashBytes,
            package.Signature,
            "更新包哈希");
    }

    private void VerifyRsaSignature(
        byte[] content,
        string signatureBase64,
        string targetName)
    {
        if (string.IsNullOrWhiteSpace(
                _rsaPublicKeyPem))
        {
            throw new UpdateSecurityException(
                "未配置更新签名公钥。");
        }

        byte[] signature;

        try
        {
            signature =
                Convert.FromBase64String(
                    signatureBase64);
        }
        catch (FormatException exception)
        {
            throw new UpdateSecurityException(
                $"{targetName}签名格式无效。",
                exception);
        }

        using RSA rsa =
            RSA.Create();

        try
        {
            rsa.ImportFromPem(
                _rsaPublicKeyPem);
        }
        catch (Exception exception)
        {
            throw new UpdateSecurityException(
                "RSA 公钥无法加载。",
                exception);
        }

        bool valid =
            rsa.VerifyData(
                content,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);

        if (!valid)
        {
            throw new UpdateSecurityException(
                $"{targetName}数字签名验证失败。");
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream =
            new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

        using SHA256 sha256 =
            SHA256.Create();

        byte[] hash =
            await sha256.ComputeHashAsync(
                stream,
                cancellationToken);

        return Convert.ToHexString(hash);
    }

    private static string NormalizeHex(
        string value)
    {
        string normalized =
            value
                .Replace(
                    "-",
                    string.Empty,
                    StringComparison.Ordinal)
                .Trim()
                .ToUpperInvariant();

        if (normalized.Length != 64 ||
            normalized.Any(
                character =>
                    !Uri.IsHexDigit(character)))
        {
            throw new UpdateSecurityException(
                "更新包 SHA-256 值无效。");
        }

        return normalized;
    }
}

public sealed class UpdateSecurityException :
    Exception
{
    public UpdateSecurityException(
        string message)
        : base(message)
    {
    }

    public UpdateSecurityException(
        string message,
        Exception innerException)
        : base(
            message,
            innerException)
    {
    }
}
