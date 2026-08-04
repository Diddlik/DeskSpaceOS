using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using DeskSpaceOS.Core.Storage;
using Microsoft.Extensions.Logging;

namespace DeskSpaceOS.Service;

public class OverlayManager
{
    private readonly ILogger _logger;
    private readonly SettingsWatcher _settingsWatcher;
    private Thread? _uiThread;
    private OverlayWindow? _overlayWindow;
    private System.Windows.Application? _app;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;

    public OverlayWindow? OverlayWindow => _overlayWindow;

    public OverlayManager(ILogger logger, SettingsWatcher settingsWatcher)
    {
        _logger = logger;
        _settingsWatcher = settingsWatcher;
    }

    public void StartOverlay(IntPtr workerWHandle)
    {
        if (_uiThread != null && _uiThread.IsAlive)
        {
            _logger.LogWarning("UI Thread is already running.");
            return;
        }

        _logger.LogInformation("Starting UI Thread for Overlay...");

        _uiThread = new Thread(() =>
        {
            _app = new System.Windows.Application();
            _overlayWindow = new OverlayWindow(workerWHandle, _settingsWatcher, _logger);

            InitializeTrayIcon();

            _app.Run(_overlayWindow);

            _notifyIcon?.Dispose();
            _logger.LogInformation("UI Thread exited.");
        });

        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.IsBackground = true;
        _uiThread.Start();
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon();
        
        // Use a default system icon or try to load a custom one
        try
        {
            _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        _notifyIcon.Text = "DeskSpaceOS Service";
        _notifyIcon.Visible = true;

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        var exitMenuItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitMenuItem.Click += (s, e) =>
        {
            _logger.LogInformation("Exit requested from tray icon.");
            Environment.Exit(0);
        };
        contextMenu.Items.Add(exitMenuItem);
        _notifyIcon.ContextMenuStrip = contextMenu;

        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                OpenSettingsApp();
            }
        };

        // Double-click is the conventional gesture for opening a tray app's window.
        _notifyIcon.MouseDoubleClick += (s, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                OpenSettingsApp();
            }
        };
    }

    private void OpenSettingsApp()
    {
        // Always launch the Settings App executable. Its built-in single-instance
        // guard (mutex + activation named pipe in App.OnLaunched) reliably brings an
        // already-running window to the foreground via WindowNative.GetWindowHandle,
        // which is dependable for WinUI 3 windows — unlike Process.MainWindowHandle,
        // which frequently resolved to Zero and caused tray clicks to be ignored.
        try
        {
            string exeName = "DeskSpaceOS.SettingsApp.exe";
            string serviceDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // Check same directory (Production)
            string prodPath = Path.Combine(serviceDir, exeName);
            if (File.Exists(prodPath))
            {
                Process.Start(new ProcessStartInfo(prodPath) { UseShellExecute = true });
                return;
            }

            // Check relative path for dev environment
            DirectoryInfo? currentDir = new DirectoryInfo(serviceDir);
            while (currentDir != null && currentDir.Name != "DeskSpaceOS.Service")
            {
                currentDir = currentDir.Parent;
            }

            if (currentDir != null && currentDir.Parent != null)
            {
                string devPathSearch = Path.Combine(currentDir.Parent.FullName, "DeskSpaceOS.SettingsApp", "bin");
                var files = Directory.GetFiles(devPathSearch, exeName, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    Process.Start(new ProcessStartInfo(files[0]) { UseShellExecute = true });
                    return;
                }
            }

            _logger.LogWarning("Could not find Settings App executable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Settings App.");
        }
    }

    public void StopOverlay()
    {
        if (_app != null && _uiThread != null && _uiThread.IsAlive)
        {
            _app.Dispatcher.Invoke(() =>
            {
                _notifyIcon?.Dispose();
                _app.Shutdown();
            });
            _uiThread.Join();
        }
    }
}