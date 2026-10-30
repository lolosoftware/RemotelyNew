using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Remotely.Shared.Helpers;

public static class InstallationPolicy
{
    public static string NormalizeServer(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            throw new InvalidOperationException("Specify the HTTPS origin of the server, without a path or credentials.");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static string MergeConfiguration(string? existing, string server, string organization)
    {
        server = NormalizeServer(server);
        if (string.IsNullOrWhiteSpace(organization) || organization.Length > 128 ||
            organization.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            throw new InvalidOperationException("Invalid organization identifier.");
        if (existing is not null)
        {
            var config = JsonNode.Parse(existing)?.AsObject() ?? throw new InvalidOperationException("Invalid existing configuration.");
            if (NormalizeServer(config["Host"]?.GetValue<string>() ?? "") != server ||
                config["OrganizationID"]?.GetValue<string>() != organization ||
                string.IsNullOrWhiteSpace(config["DeviceID"]?.GetValue<string>()))
                throw new InvalidOperationException("The existing server, organization or device identity does not match. Migration was stopped to preserve the configuration.");
            return existing; // Preserve identity, verification token and unknown extension properties byte-for-byte.
        }
        return new JsonObject { ["Host"] = server, ["OrganizationID"] = organization,
            ["DeviceID"] = Guid.NewGuid().ToString() }.ToJsonString();
    }

    public static void ExtractPackage(string archivePath, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count > 25_000) throw new InvalidDataException("Too many package files.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            total = checked(total + entry.Length);
            if (total > 2L * 1024 * 1024 * 1024 || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Package limits exceeded or symbolic link found.");
            var name = entry.FullName.Replace('\\', '/');
            if (name.Contains(':') || name.StartsWith('/') || name.Split('/').Contains(".."))
                throw new InvalidDataException("Unsafe package path.");
            var path = Path.GetFullPath(Path.Combine(root, name));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !paths.Add(path))
                throw new InvalidDataException("Unsafe or duplicate package path.");
            if (name.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            if (name.Equals("ConnectionInfo.json", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Installer/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The package attempts to overwrite local configuration.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew);
            var buffer = new byte[81920];
            long copied = 0;
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                copied += count;
                if (copied > entry.Length) throw new InvalidDataException("Invalid expanded file length.");
                output.Write(buffer, 0, count);
            }
            if (copied != entry.Length) throw new InvalidDataException("Incomplete expanded file.");
        }
    }
}
