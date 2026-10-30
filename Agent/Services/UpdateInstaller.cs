using System;
using System.IO;
using System.Threading.Tasks;

namespace Remotely.Agent.Services;

internal static class UpdateInstaller
{
    // Substitute deployment data only after the publisher's raw template has been verified.
    public static async Task ConfigureUnixTemplate(string path, string host, string organization)
    {
        var text = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
        if (!text.Contains("HostName=\n") || !text.Contains("Organization=\n"))
            throw new InvalidOperationException("Installer template is invalid.");
        static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
        text = text.Replace("HostName=\n", "HostName=" + Quote(host) + "\n")
            .Replace("Organization=\n", "Organization=" + Quote(organization) + "\n");
        await File.WriteAllTextAsync(path, text);
    }
}
