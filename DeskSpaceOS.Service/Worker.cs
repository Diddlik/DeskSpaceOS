using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DeskSpaceOS.Core.Storage;
using DeskSpaceOS.Core.Win32;

namespace DeskSpaceOS.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly SettingsWatcher _settingsWatcher = new();
    private IntPtr _workerWHandle = IntPtr.Zero;
    private IntPtr _listViewHandle = IntPtr.Zero;
    private bool _wasAutoArrangeEnabled = false;
    private bool _autoArrangeDisabledByUs = false;
    private OverlayManager _overlayManager;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _overlayManager = new OverlayManager(logger, _settingsWatcher);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to initialize Desktop Hook (Progman/WorkerW)...");
        try
        {
            _workerWHandle = DesktopManager.InitializeDesktopHook();
            if (_workerWHandle != IntPtr.Zero)
            {
                _logger.LogInformation($"Successfully located and spawned WorkerW. Handle: {_workerWHandle}");

                // Start the WPF Overlay
                _overlayManager.StartOverlay(_workerWHandle);
            }
            else
            {
                _logger.LogWarning("WorkerW initialization returned an empty handle.");
            }

            _listViewHandle = DesktopManager.GetDesktopListViewHandle();
            if (_listViewHandle != IntPtr.Zero)
            {
                _logger.LogInformation($"Successfully located SysListView32. Handle: {_listViewHandle}");

                int itemCount = ListViewManager.GetItemCount(_listViewHandle);
                _logger.LogInformation($"Found {itemCount} desktop icons.");

                // Disable auto-arrange if configured (default: true)
                var appSettings = AppSettingsStore.Load();
                if (appSettings.DisableAutoArrange)
                {
                    _wasAutoArrangeEnabled = ListViewManager.IsAutoArrangeEnabled(_listViewHandle);
                    if (_wasAutoArrangeEnabled)
                    {
                        ListViewManager.SetAutoArrange(_listViewHandle, false);
                        _autoArrangeDisabledByUs = true;
                        _logger.LogInformation("Disabled desktop icon auto-arrange.");
                    }
                }
            }
            else
            {
                _logger.LogWarning("Could not find SysListView32.");
            }

            // Start watching settings files for hot-reload
            _settingsWatcher.SettingsChanged += OnSettingsChanged;
            _settingsWatcher.Start();
            _logger.LogInformation("Settings file watcher started.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Desktop Hook.");
        }

        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping overlay...");
        _settingsWatcher.SettingsChanged -= OnSettingsChanged;
        _settingsWatcher.Dispose();

        _overlayManager?.StopOverlay();

        // Restore auto-arrange if we disabled it
        if (_autoArrangeDisabledByUs && _listViewHandle != IntPtr.Zero)
        {
            ListViewManager.SetAutoArrange(_listViewHandle, true);
            _logger.LogInformation("Restored desktop icon auto-arrange.");
        }

        return base.StopAsync(cancellationToken);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        try
        {
            var settings = AppSettingsStore.Load();

            if (_listViewHandle != IntPtr.Zero)
            {
                if (settings.DisableAutoArrange && !_autoArrangeDisabledByUs)
                {
                    // Setting turned ON — disable auto-arrange
                    if (ListViewManager.IsAutoArrangeEnabled(_listViewHandle))
                    {
                        ListViewManager.SetAutoArrange(_listViewHandle, false);
                        _autoArrangeDisabledByUs = true;
                        _logger.LogInformation("Hot-reload: disabled auto-arrange.");
                    }
                }
                else if (!settings.DisableAutoArrange && _autoArrangeDisabledByUs)
                {
                    // Setting turned OFF — restore auto-arrange
                    ListViewManager.SetAutoArrange(_listViewHandle, true);
                    _autoArrangeDisabledByUs = false;
                    _logger.LogInformation("Hot-reload: restored auto-arrange.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to hot-reload settings.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Worker running at: {time}", DateTimeOffset.Now);
            }
            await Task.Delay(10000, stoppingToken);
        }
    }
}