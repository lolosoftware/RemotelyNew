using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Shared.Helpers;
using System;
using System.Security.Cryptography;
using System.Text.Json;

namespace Remotely.Shared.Tests;

[TestClass]
public class UpdateSignatureVerifierTests
{
    [TestMethod]
    public void AcceptsSignedArtifactAndRejectsTamperingRollbackAndExpiry()
    {
        using var rsa = RSA.Create(3072);
        var content = new byte[] { 1, 2, 3, 4 };
        var now = DateTimeOffset.UtcNow;
        var manifest = new UpdateArtifactManifest(20, "agent.zip", Convert.ToHexString(SHA256.HashData(content)), content.Length, now.AddDays(1));
        var payload = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var key = rsa.ExportSubjectPublicKeyInfoPem();
        var verified = UpdateSignatureVerifier.VerifyManifest(payload, signature, key, "agent.zip", 19, now);
        UpdateSignatureVerifier.VerifyHash(verified, SHA256.HashData(content), content.Length);
        Assert.ThrowsException<CryptographicException>(() => UpdateSignatureVerifier.VerifyManifest(payload, signature, key, "other.zip", 19, now));
        Assert.ThrowsException<CryptographicException>(() => UpdateSignatureVerifier.VerifyManifest(payload, signature, key, "agent.zip", 21, now));
        Assert.ThrowsException<CryptographicException>(() => UpdateSignatureVerifier.VerifyManifest(payload, signature, key, "agent.zip", 19, now.AddDays(2)));
        content[0] ^= 1;
        Assert.ThrowsException<CryptographicException>(() => UpdateSignatureVerifier.VerifyHash(verified, SHA256.HashData(content), content.Length));
        payload[0] ^= 1;
        Assert.ThrowsException<CryptographicException>(() => UpdateSignatureVerifier.VerifyManifest(payload, signature, key, "agent.zip", 19, now));
    }
}
