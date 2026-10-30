using Remotely.Shared.Helpers;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Remotely.Agent.Services;

public interface IUpdateDownloader { Task DownloadFile(string address, string fileName); }

/// <summary>Updates require a locally provisioned trust key; never trust a key downloaded with an update.</summary>
public class UpdateDownloader : IUpdateDownloader
{
    private static readonly SemaphoreSlim UpdateLock = new(1, 1);
    public static string TrustDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Remotely", "UpdateTrust")
        : "/etc/remotely/update-trust";

    public async Task DownloadFile(string downloadUrl, string filePath)
    {
        var uri = new Uri(downloadUrl, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Updates require a canonical HTTPS artifact URL.");
        var artifact = CanonicalArtifact(uri);
        var keyPath = Path.Combine(TrustDirectory, "public-key.pem");
        if (!File.Exists(keyPath)) throw new InvalidOperationException("Update trust key has not been provisioned.");
        var publicKey = await File.ReadAllTextAsync(keyPath);
        var statePath = Path.Combine(TrustDirectory, "release-sequence.txt");
        await UpdateLock.WaitAsync();
        var temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            var minimum = 0L;
            if (File.Exists(statePath) && !long.TryParse(await File.ReadAllTextAsync(statePath), out minimum))
                throw new InvalidOperationException("Update trust state is invalid.");
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var manifestBytes = await ReadMetadata(client, artifact.AbsoluteUri + ".manifest.json", 16_384, cancellation.Token);
            var signature = await ReadMetadata(client, artifact.AbsoluteUri + ".sig", 1024, cancellation.Token);
            var name = Uri.UnescapeDataString(Path.GetFileName(artifact.AbsolutePath));
            var manifest = UpdateSignatureVerifier.VerifyManifest(manifestBytes, signature, publicKey, name, minimum, DateTimeOffset.UtcNow);
            using var response = await client.GetAsync(artifact, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != manifest.Length)
                throw new CryptographicException("Unexpected update length.");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation.Token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81_920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellation.Token)) != 0)
                {
                    total += count;
                    if (total > manifest.Length) throw new CryptographicException("Update exceeded its signed length.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellation.Token);
                }
                UpdateSignatureVerifier.VerifyHash(manifest, hash.GetHashAndReset(), total);
            }
            File.Move(temporary, filePath, overwrite: true);
            var stateTemporary = statePath + ".new";
            await File.WriteAllTextAsync(stateTemporary, manifest.ReleaseSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(stateTemporary, statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            UpdateLock.Release();
        }
    }

    private static Uri CanonicalArtifact(Uri source)
    {
        const string prefix = "/api/AgentUpdate/DownloadPackage/";
        if (!source.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return source;
        var platform = source.AbsolutePath[prefix.Length..].ToLowerInvariant();
        var name = platform switch
        {
            "win-x64" => "Remotely-Win-x64.zip", "win-x86" => "Remotely-Win-x86.zip",
            "linux" => "Remotely-Linux.zip", "macos-x64" => "Remotely-MacOS-x64.zip",
            "macos-arm64" => "Remotely-MacOS-arm64.zip", _ => throw new InvalidOperationException("Unknown update platform.")
        };
        return new Uri(source, "/Content/" + name);
    }

    private static async Task<byte[]> ReadMetadata(HttpClient client, string address, int limit, CancellationToken token)
    {
        using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[1024];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > limit) throw new CryptographicException("Update metadata exceeds its limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
