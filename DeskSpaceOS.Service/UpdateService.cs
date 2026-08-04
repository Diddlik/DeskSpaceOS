using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace DeskSpaceOS.Service;

public class UpdateService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);

    private readonly ILogger<UpdateService> _logger;
    private readonly IConfiguration _config;

    public UpdateService(ILogger<UpdateService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await TryApplyUpdateAsync(stoppingToken);
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task TryApplyUpdateAsync(CancellationToken ct)
    {
        try
        {
            var source = BuildUpdateSource();
            if (source is null) return;

            var mgr = new UpdateManager(source);

            if (!mgr.IsInstalled)
            {
                _logger.LogDebug("Not a Velopack install — skipping update check.");
                return;
            }

            var update = await mgr.CheckForUpdatesAsync();
            if (update is null)
            {
                _logger.LogDebug("No updates available.");
                return;
            }

            _logger.LogInformation("Update {Version} available — downloading.", update.TargetFullRelease.Version);
            await mgr.DownloadUpdatesAsync(update, cancelToken: ct);

            _logger.LogInformation("Download complete. Applying update and exiting.");
            mgr.ApplyUpdatesAndExit(update.TargetFullRelease);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed — will retry in {Hours}h.", CheckInterval.TotalHours);
        }
    }

    // The product's canonical release repository. Used when Updates:Url is not
    // configured so a default install auto-updates without shipping extra config.
    // Override via Updates:Url (e.g. a local folder / file:// URI for testing).
    private const string DefaultUpdateUrl = "https://github.com/Diddlik/DeskSpaceOS";

    private IUpdateSource? BuildUpdateSource()
    {
        string? url = _config["Updates:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            url = DefaultUpdateUrl;
        }

        // Local folder or file:// URI → SimpleFileSource
        if (!url.StartsWith("https://github.com", StringComparison.OrdinalIgnoreCase))
        {
            // Accept both "file:///C:/path" and plain "C:\path"
            string localPath = url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(url).LocalPath
                : url;
            return new SimpleFileSource(new DirectoryInfo(localPath));
        }

        // GitHub releases
        string? token = _config["Updates:GitHubToken"];
        return new GithubSource(url, token, prerelease: false);
    }
}
