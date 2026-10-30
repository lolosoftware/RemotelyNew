using Remotely.Shared.Utilities;
using System;
using System.Diagnostics;
using System.IO;

namespace Remotely.Agent.Services;

public interface IUninstaller
{
    void UninstallAgent();
}

public class Uninstaller : IUninstaller
{
    public void UninstallAgent()
    {
        if (EnvironmentHelper.IsWindows)
        {
            var installer = Path.Combine(AppContext.BaseDirectory, "Installer", "Remotely_Install.exe");
            if (!File.Exists(installer))
                throw new InvalidOperationException("Use the native setup bundle to uninstall this legacy installation safely.");
            var process = new ProcessStartInfo(installer) { UseShellExecute = false, CreateNoWindow = true };
            process.ArgumentList.Add("--quiet"); process.ArgumentList.Add("--uninstall");
            Process.Start(process);
        }
        else if (EnvironmentHelper.IsLinux)
        {
            Process.Start("sudo", "systemctl stop remotely-agent").WaitForExit();
            Directory.Delete("/usr/local/bin/Remotely", true);
            File.Delete("/etc/systemd/system/remotely-agent.service");
            Process.Start("sudo", "systemctl daemon-reload").WaitForExit();
        }
        Environment.Exit(0);
    }
}
