using Microsoft.Win32;
using Remotely.Shared.Helpers;
using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json.Nodes;

namespace Remotely.Install;

public static class InstallerEngine
{
    public const string ServiceName = "Remotely_Service";
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Remotely");
    public static string TrustRoot => Path.Combine(DataRoot, "UpdateTrust");
    private static string DefaultInstall => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Remotely");
    public static string LogPath => Path.Combine(DataRoot, "InstallerLogs", "install-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".log");
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Remotely";

    public static string ExistingDirectory()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ServiceName);
        if (key?.GetValue("ImagePath") is not string image) return DefaultInstall;
        var exe = image.StartsWith('"') ? image.Split('"')[1] : image[..(image.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) + 4)];
        if (!Path.IsPathFullyQualified(exe) || !Path.GetFileName(exe).Equals("Remotely_Agent.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The existing service has an unexpected executable path. No files were changed.");
        return Path.GetDirectoryName(exe)!;
    }

    public static string? ExistingConfiguration()
    {
        var path = Path.Combine(ExistingDirectory(), "ConnectionInfo.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static string PinnedKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UpdatePublicKey.pem")
            ?? throw new InvalidOperationException("The installer has no pinned signing key.");
        using var reader = new StreamReader(stream);
        var pem = reader.ReadToEnd();
        using var rsa = RSA.Create(); rsa.ImportFromPem(pem);
        if (rsa.KeySize < 3072) throw new InvalidOperationException("The pinned key is too short.");
        return pem;
    }

    private static void RejectReparsePoints(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Installation paths cannot contain junctions or symbolic links.");
    }

    public static void ProtectDirectory(string path, bool allowUsersRead = false)
    {
        RejectReparsePoints(path);
        Directory.CreateDirectory(path);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        if (allowUsersRead) acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl);
    }

    public static UpdateArtifactManifest Verify(string package, string publicKey)
    {
        var minimum = 0L;
        var sequence = Path.Combine(TrustRoot, "release-sequence.txt");
        if (File.Exists(sequence) && !long.TryParse(File.ReadAllText(sequence), out minimum))
            throw new InvalidDataException("Invalid installed release sequence.");
        if (new FileInfo(package + ".manifest.json").Length > 16384 || new FileInfo(package + ".sig").Length > 1024)
            throw new InvalidDataException("Oversized signature metadata.");
        var manifest = UpdateSignatureVerifier.VerifyManifest(File.ReadAllBytes(package + ".manifest.json"),
            File.ReadAllBytes(package + ".sig"), publicKey, Path.GetFileName(package), minimum, DateTimeOffset.UtcNow);
        using var content = File.OpenRead(package);
        UpdateSignatureVerifier.VerifyHash(manifest, SHA256.HashData(content), content.Length);
        return manifest;
    }

    // Keep verification and extraction on the same administrator-owned copy. A ZIP in Downloads
    // must not be replaceable by an unprivileged process after its hash has been verified.
    private sealed class ProtectedPackage : IDisposable
    {
        private readonly string _directory;
        public string Path { get; }
        public ProtectedPackage(string source)
        {
            _directory = System.IO.Path.Combine(DataRoot, "Package-" + Guid.NewGuid().ToString("N"));
            ProtectDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, System.IO.Path.GetFileName(source));
            try
            {
                foreach (var (suffix, limit) in new[] { ("", UpdateSignatureVerifier.MaxArtifactLength), (".manifest.json", 16384L), (".sig", 1024L) })
                {
                    using var input = new FileStream(source + suffix, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (input.Length > limit) throw new InvalidDataException("Oversized package or metadata.");
                    using var output = new FileStream(Path + suffix, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    input.CopyTo(output);
                }
            }
            catch { Directory.Delete(_directory, true); throw; }
        }
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    private static bool ServiceExists() => ServiceController.GetServices().Any(x => { using (x) return x.ServiceName == ServiceName; });

    private static void StopService()
    {
        if (!ServiceExists()) return;
        using var service = new ServiceController(ServiceName);
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            service.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45));
        }
    }

    private static async Task RunSc(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start service control.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0) throw new IOException("Service control failed: " + await output + await error);
    }

    public static async Task Install(string package, string server, string organization, Action<string> log, string? etag = null, bool allowLoopbackHttp = false)
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Run the installer as administrator.");
        ProtectDirectory(DataRoot);
        using var installationLock = new FileStream(Path.Combine(DataRoot, "installer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var directory = ExistingDirectory(); RejectReparsePoints(directory);
        var hadService = ServiceExists();
        using (var settings = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ServiceName))
        {
            if (hadService && settings?.GetValue("ObjectName") is string account && !account.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This service uses a custom account. Preserve it and use a manually qualified migration.");
            if (hadService && settings?.GetValue("Start") is int startMode && startMode == 4)
                throw new InvalidOperationException("The existing service is disabled. Its startup policy was not changed.");
        }
        if ((hadService || Directory.Exists(directory)) && !File.Exists(Path.Combine(directory, "ConnectionInfo.json")))
            throw new InvalidOperationException("An existing installation has no configuration. Restore it before continuing; no new identity was created.");
        var configuration = InstallationPolicy.MergeConfiguration(ExistingConfiguration(), server, organization, allowLoopbackHttp);
        var publicKey = PinnedKey();
        if (File.Exists(Path.Combine(TrustRoot, "public-key.pem")))
        {
            using var current = RSA.Create(); current.ImportFromPem(File.ReadAllText(Path.Combine(TrustRoot, "public-key.pem")));
            using var next = RSA.Create(); next.ImportFromPem(publicKey);
            if (!current.ExportSubjectPublicKeyInfo().SequenceEqual(next.ExportSubjectPublicKeyInfo()))
                throw new InvalidOperationException("The installed signing key differs. Key rotation must be performed explicitly.");
        }
        using var securePackage = new ProtectedPackage(package);
        package = securePackage.Path;
        var manifest = Verify(package, publicKey);
        var expected = Environment.Is64BitOperatingSystem ? "Remotely-Win-x64.zip" : "Remotely-Win-x86.zip";
        if (manifest.FileName != expected) throw new InvalidDataException("The package architecture does not match this computer.");
        ProtectDirectory(DataRoot);
        var transaction = Guid.NewGuid().ToString("N");
        var stage = directory + ".stage-" + transaction;
        var backup = directory + ".rollback-" + transaction;
        var originalImage = "";
        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ServiceName))
            originalImage = key?.GetValue("ImagePath") as string ?? "";
        var wasRunning = false;
        if (hadService) { using var service = new ServiceController(ServiceName); wasRunning = service.Status == ServiceControllerStatus.Running; }
        var movedOld = false; var movedNew = false; var createdService = false; var touchedService = false;
        try
        {
            log("Verifying and extracting the signed offline package.");
            ProtectDirectory(stage, allowUsersRead: true); InstallationPolicy.ExtractPackage(package, stage);
            if (!File.Exists(Path.Combine(stage, "Remotely_Agent.exe")) || !File.Exists(Path.Combine(stage, "Desktop", "Remotely_Desktop.exe")))
                throw new InvalidDataException("The package is incomplete.");
            File.WriteAllText(Path.Combine(stage, "ConnectionInfo.json"), configuration);
            if (!string.IsNullOrWhiteSpace(etag) && System.Net.Http.Headers.EntityTagHeaderValue.TryParse(etag, out _))
                File.WriteAllText(Path.Combine(stage, "etag.txt"), etag);
            var installer = Path.Combine(stage, "Installer"); Directory.CreateDirectory(installer);
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
                if (!file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".sig", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
                    File.Copy(file, Path.Combine(installer, Path.GetFileName(file)), true);
            log("Stopping the existing service. Configuration and device identity are preserved.");
            StopService(); touchedService = true;
            if (Directory.Exists(directory)) { Directory.Move(directory, backup); movedOld = true; log("Rollback copy: " + backup); }
            Directory.Move(stage, directory); movedNew = true;
            var image = "\"" + Path.Combine(directory, "Remotely_Agent.exe") + "\"";
            if (!hadService) { await RunSc("create", ServiceName, "binPath=", image, "start=", "auto", "DisplayName=", "Remotely Service"); createdService = true; }
            if (!hadService) await RunSc("failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/60000");
            ProtectDirectory(TrustRoot);
            // Provision trust before the service starts its update check. Restore it if installation fails.
            var trustFile = Path.Combine(TrustRoot, "public-key.pem");
            var hadKey = File.Exists(trustFile);
            File.WriteAllText(trustFile, publicKey);
            try
            {
                if (!hadService || wasRunning)
                {
                    using var service = new ServiceController(ServiceName); service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45));
                    await Task.Delay(3000); service.Refresh();
                    if (service.Status != ServiceControllerStatus.Running) throw new IOException("The agent did not remain running.");
                }
                else log("The existing service was stopped; its stopped state was preserved.");
            }
            catch { if (!hadKey) File.Delete(trustFile); throw; }
            using var uninstall = Registry.LocalMachine.CreateSubKey(UninstallKey);
            uninstall.SetValue("DisplayName", "Remotely remote support agent");
            uninstall.SetValue("Publisher", "Remotely autonomous edition");
            uninstall.SetValue("InstallLocation", directory);
            uninstall.SetValue("UninstallString", "\"" + Path.Combine(directory, "Installer", "Remotely_Install.exe") + "\" --uninstall");
            uninstall.SetValue("NoModify", 1); uninstall.SetValue("NoRepair", 1);
            File.WriteAllText(Path.Combine(TrustRoot, "release-sequence.txt"), manifest.ReleaseSequence.ToString());
            log("Installation completed. Check the service and the device in Home; service startup alone does not prove server connectivity.");
        }
        catch
        {
            log("Installation failed; restoring the previous files and configuration.");
            if (movedNew)
            {
                StopService();
                Directory.Move(directory, directory + ".failed-" + transaction);
            }
            if (movedOld) Directory.Move(backup, directory);
            if (createdService && ServiceExists()) await RunSc("delete", ServiceName);
            if (hadService && touchedService)
            {
                await RunSc("config", ServiceName, "binPath=", originalImage);
                if (wasRunning) { using var service = new ServiceController(ServiceName); service.Start(); }
            }
            throw;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    public static async Task Uninstall(Action<string> log)
    {
        var directory = ExistingDirectory(); RejectReparsePoints(directory); ProtectDirectory(DataRoot);
        using var installationLock = new FileStream(Path.Combine(DataRoot, "installer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var configuration = Path.Combine(directory, "ConnectionInfo.json");
        if (File.Exists(configuration))
        {
            var backup = Path.Combine(DataRoot, "ConfigurationBackups"); ProtectDirectory(backup);
            File.Copy(configuration, Path.Combine(backup, "ConnectionInfo-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json"));
        }
        StopService();
        if (ServiceExists()) await RunSc("delete", ServiceName);
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);
        // Keep binaries and configuration for recovery; no recursive deletion while executing from the installation.
        log("Service uninstalled. Configuration, signing trust and files were retained at " + directory + ". They may be removed manually after backup.");
    }
}
