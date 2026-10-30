using System.Security.Cryptography;
using System.Text.Json;

namespace Remotely.Shared.Helpers;

public sealed record UpdateArtifactManifest(long ReleaseSequence, string FileName, string Sha256,
    long Length, DateTimeOffset ExpiresUtc);

public static class UpdateSignatureVerifier
{
    public const long MaxArtifactLength = 512L * 1024 * 1024;

    public static UpdateArtifactManifest VerifyManifest(byte[] payload, byte[] signature,
        string trustedPublicKeyPem, string expectedFileName, long minimumSequence, DateTimeOffset now)
    {
        if (payload.Length > 16_384 || signature.Length > 1024)
            throw new CryptographicException("Update metadata exceeds its limit.");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(trustedPublicKeyPem);
        if (rsa.KeySize < 3072 || !rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException("Update signature is invalid.");
        var manifest = JsonSerializer.Deserialize<UpdateArtifactManifest>(payload)
            ?? throw new CryptographicException("Update manifest is empty.");
        if (manifest.FileName != expectedFileName || manifest.ReleaseSequence < minimumSequence ||
            manifest.ReleaseSequence <= 0 || manifest.Length <= 0 || manifest.Length > MaxArtifactLength ||
            manifest.ExpiresUtc <= now || manifest.Sha256 is null || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
            throw new CryptographicException("Update manifest does not satisfy the trust policy.");
        return manifest;
    }

    public static void VerifyHash(UpdateArtifactManifest manifest, byte[] sha256, long length)
    {
        if (length != manifest.Length || !CryptographicOperations.FixedTimeEquals(
            sha256, Convert.FromHexString(manifest.Sha256)))
            throw new CryptographicException("Update artifact does not match its signed manifest.");
    }
}
