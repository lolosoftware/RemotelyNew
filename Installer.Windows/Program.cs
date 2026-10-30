using System.Text.Json.Nodes;

namespace Remotely.Install;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) return 2;
            var name = args[i][2..];
            if (name is "quiet" or "uninstall" or "verify-only") values[name] = "true";
            else if (++i < args.Length) values[name] = args[i]; else return 2;
        }
        if (!values.ContainsKey("quiet") && !values.ContainsKey("verify-only"))
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new InstallerForm(values));
            return Environment.ExitCode;
        }
        try
        {
            var key = InstallerEngine.PinnedKey();
            var package = values.GetValueOrDefault("package") ?? Path.Combine(AppContext.BaseDirectory,
                Environment.Is64BitOperatingSystem ? "Remotely-Win-x64.zip" : "Remotely-Win-x86.zip");
            if (values.ContainsKey("verify-only")) { InstallerEngine.Verify(package, key); return 0; }
            InstallerEngine.ProtectDirectory(InstallerEngine.DataRoot);
            InstallerEngine.ProtectDirectory(Path.Combine(InstallerEngine.DataRoot, "InstallerLogs"));
            var logPath = InstallerEngine.LogPath;
            void Log(string text) => File.AppendAllText(logPath, DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine);
            try
            {
                if (values.ContainsKey("uninstall")) InstallerEngine.Uninstall(Log).GetAwaiter().GetResult();
                else InstallerEngine.Install(package, values.GetValueOrDefault("server") ?? "",
                    values.GetValueOrDefault("organization") ?? "", Log, values.GetValueOrDefault("etag")).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex) { Log(ex.ToString()); return 1; }
        }
        catch { return 1; }
    }
}

internal sealed class InstallerForm : Form
{
    private readonly TextBox _server = new() { Dock = DockStyle.Fill, PlaceholderText = "https://support.example.com" };
    private readonly TextBox _organization = new() { Dock = DockStyle.Fill };
    private readonly TextBox _package = new() { Dock = DockStyle.Fill };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Button _install = new() { Text = "Install / update", AutoSize = true };
    private readonly bool _uninstall;
    private bool _busy;

    public InstallerForm(Dictionary<string,string> options)
    {
        Text = "Remotely — agent setup"; Width = 780; Height = 570; MinimumSize = new Size(650, 450);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10);
        _uninstall = options.ContainsKey("uninstall");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(string label, Control control) { var row = layout.Controls.Count / 2; layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left },0,row); layout.Controls.Add(control,1,row); }
        Add("HTTPS server", _server); Add("Organization ID", _organization); Add("Offline ZIP package", _package);
        _server.AccessibleName = "HTTPS server"; _organization.AccessibleName = "Organization ID"; _package.AccessibleName = "Offline ZIP package";
        var browse = new Button { Text = "Choose package…", AutoSize = true };
        browse.Click += (_,_) => { using var picker = new OpenFileDialog { Filter = "Remotely package (*.zip)|*.zip" }; if(picker.ShowDialog(this) == DialogResult.OK) _package.Text = picker.FileName; };
        Add("", browse);
        Add("", new Label { Text = "Existing device identity and configuration are preserved. Active remote sessions will be interrupted during update.", AutoSize = true, MaximumSize = new Size(500,80) });
        Add("", _install); Add("Setup log", _log);
        for (var i=0; i<6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        _package.Text = options.GetValueOrDefault("package") ?? Path.Combine(AppContext.BaseDirectory,
            Environment.Is64BitOperatingSystem ? "Remotely-Win-x64.zip" : "Remotely-Win-x86.zip");
        _server.Text = options.GetValueOrDefault("server") ?? ""; _organization.Text = options.GetValueOrDefault("organization") ?? "";
        try
        {
            var existing = InstallerEngine.ExistingConfiguration();
            if(existing is not null)
            {
                var config = JsonNode.Parse(existing)!;
                _server.Text = config["Host"]?.GetValue<string>() ?? "";
                _organization.Text = config["OrganizationID"]?.GetValue<string>() ?? "";
                _server.ReadOnly = _organization.ReadOnly = true;
                _log.Text = "Existing installation detected: " + InstallerEngine.ExistingDirectory() + Environment.NewLine;
            }
        }
        catch(Exception ex) { _log.Text = ex.Message; _install.Enabled = false; }
        if (_uninstall) { _install.Text = "Uninstall service"; _server.Enabled = _organization.Enabled = _package.Enabled = browse.Enabled = false; }
        _install.Click += async (_,_) => await RunInstall();
        FormClosing += (_,e) => { if(_busy) e.Cancel = true; };
    }

    private async Task RunInstall()
    {
        _busy = true; _install.Enabled = false; Environment.ExitCode = 1;
        var package = _package.Text; var server = _server.Text; var organization = _organization.Text;
        string? logPath = null;
        try
        {
            InstallerEngine.ProtectDirectory(InstallerEngine.DataRoot);
            InstallerEngine.ProtectDirectory(Path.Combine(InstallerEngine.DataRoot,"InstallerLogs"));
            logPath = InstallerEngine.LogPath;
            void Log(string text)
            {
                File.AppendAllText(logPath, DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine);
                BeginInvoke(() => _log.AppendText(text + Environment.NewLine));
            }
            await Task.Run(async () => {
                if (_uninstall) await InstallerEngine.Uninstall(Log);
                else await InstallerEngine.Install(package, server, organization, Log);
            });
            Environment.ExitCode = 0;
            _log.AppendText("Done. Log: " + logPath + Environment.NewLine);
        }
        catch(Exception ex)
        {
            if(logPath is not null) File.AppendAllText(logPath, ex + Environment.NewLine);
            _log.AppendText("Failed: " + ex.Message + Environment.NewLine + "Log: " + logPath + Environment.NewLine);
        }
        finally { _busy = false; _install.Enabled = true; }
    }
}
