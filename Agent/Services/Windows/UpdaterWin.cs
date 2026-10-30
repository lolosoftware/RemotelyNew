using Microsoft.Extensions.Logging;
using Remotely.Agent.Interfaces;
using Remotely.Shared.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Remotely.Agent.Services.Windows;

public class UpdaterWin : IUpdater
{
    private readonly SemaphoreSlim _checkForUpdatesLock = new(1, 1);
    private readonly IConfigService _configService;
    private readonly IUpdateDownloader _updateDownloader;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpdaterWin> _logger;
    private readonly SemaphoreSlim _installLatestVersionLock = new(1, 1);
    private readonly System.Timers.Timer _updateTimer = new(TimeSpan.FromHours(6).TotalMilliseconds);
    private DateTimeOffset _lastUpdateFailure;


    public UpdaterWin(
        IConfigService configService,
        IUpdateDownloader updateDownloader,
        IHttpClientFactory httpClientFactory,
        ILogger<UpdaterWin> logger)
    {
        _configService = configService;
        _updateDownloader = updateDownloader;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task BeginChecking()
    {
        if (EnvironmentHelper.IsDebug)
        {
            return;
        }

        await CheckForUpdates();
        _updateTimer.Elapsed += UpdateTimer_Elapsed;
        _updateTimer.Start();
    }

    public async Task CheckForUpdates()
    {
        if (!await _checkForUpdatesLock.WaitAsync(0))
        {
            return;
        }

        try
        {

            if (EnvironmentHelper.IsDebug)
            {
                return;
            }

            if (_lastUpdateFailure.AddDays(1) > DateTimeOffset.Now)
            {
                _logger.LogInformation("Skipping update check due to previous failure.  Updating will be tried again after 24 hours have passed.");
                return;
            }


            var connectionInfo = _configService.GetConnectionInfo();
            if (string.IsNullOrWhiteSpace(connectionInfo.Host) || string.IsNullOrWhiteSpace(connectionInfo.OrganizationID))
                throw new InvalidOperationException("Agent connection configuration is incomplete.");
            var serverUrl = _configService.GetConnectionInfo().Host;

            var platform = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            var fileUrl = serverUrl + $"/Content/Remotely-Setup-win-{platform}.zip";

            using var httpClient = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Head, fileUrl);

            if (File.Exists("etag.txt"))
            {
                var lastEtag = await File.ReadAllTextAsync("etag.txt");
                if (!string.IsNullOrWhiteSpace(lastEtag) &&
                   EntityTagHeaderValue.TryParse(lastEtag.Trim(), out var etag))
                {
                    request.Headers.IfNoneMatch.Add(etag);
                }
            }

            using var response = await httpClient.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _logger.LogInformation("Service Updater: Version is current.");
                return;
            }


            _logger.LogInformation("Service Updater: Update found.");

            await InstallLatestVersion();

        }
        catch (WebException ex) when (ex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotModified)
        {
            _logger.LogInformation("Service Updater: Version is current.");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while checking for updates.");
        }
        finally
        {
            _checkForUpdatesLock.Release();
        }
    }

    public async Task InstallLatestVersion()
    {
        try
        {
            await _installLatestVersionLock.WaitAsync();

            var connectionInfo = _configService.GetConnectionInfo();
            if (string.IsNullOrWhiteSpace(connectionInfo.Host) || string.IsNullOrWhiteSpace(connectionInfo.OrganizationID))
                throw new InvalidOperationException("Agent connection configuration is incomplete.");
            var serverUrl = connectionInfo.Host;

            _logger.LogInformation("Service Updater: Downloading install package.");

            var updateDirectory = Directory.CreateTempSubdirectory("RemotelyUpdate-").FullName;


            var platform = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            var setupPackage = Path.Combine(updateDirectory, $"Remotely-Setup-win-{platform}.zip");
            await _updateDownloader.DownloadFile($"{serverUrl}/Content/Remotely-Setup-win-{platform}.zip", setupPackage);
            var setupDirectory = Path.Combine(updateDirectory, "setup");
            Remotely.Shared.Helpers.InstallationPolicy.ExtractPackage(setupPackage, setupDirectory);
            var installer = Path.Combine(setupDirectory, "Remotely_Install.exe");
            if (!File.Exists(installer)) throw new IOException("The signed setup bundle has no native installer.");
            var process = new ProcessStartInfo(installer) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = setupDirectory };
            foreach (var argument in new[] { "--quiet", "--server", connectionInfo.Host, "--organization", connectionInfo.OrganizationID,
                "--package", Path.Combine(setupDirectory, $"Remotely-Win-{platform}.zip") }) process.ArgumentList.Add(argument);
            if (Remotely.Shared.Helpers.InstallationPolicy.IsLoopbackHttp(new Uri(connectionInfo.Host)))
                process.ArgumentList.Add("--allow-loopback-http");
            using var headClient = _httpClientFactory.CreateClient();
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, $"{serverUrl}/Content/Remotely-Setup-win-{platform}.zip");
            using var headResponse = await headClient.SendAsync(headRequest);
            if (headResponse.Headers.ETag is { } etag)
            { process.ArgumentList.Add("--etag"); process.ArgumentList.Add(etag.ToString()); }
            Process.Start(process);
        }
        catch (WebException ex) when (ex.Status == WebExceptionStatus.Timeout)
        {
            _logger.LogWarning("Timed out while waiting to download update.");
            _lastUpdateFailure = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while installing latest version.");
            _lastUpdateFailure = DateTimeOffset.Now;
        }
        finally
        {
            _installLatestVersionLock.Release();
        }
    }

    private async void UpdateTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        await CheckForUpdates();
    }
}
