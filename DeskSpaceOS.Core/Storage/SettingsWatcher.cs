using System;
using System.IO;
using System.Threading;

namespace DeskSpaceOS.Core.Storage;

/// <summary>
/// Watches the DeskSpaceOS AppData directory for settings file changes.
/// Uses debouncing to coalesce rapid writes into a single notification.
/// </summary>
public sealed class SettingsWatcher : IDisposable
{
    private static readonly string WatchDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskSpaceOS");

    private FileSystemWatcher? _watcher;
    private Timer? _settingsDebounce;
    private Timer? _spacesDebounce;
    private Timer? _portalsDebounce;
    private Timer? _sortingRulesDebounce;

    private const int DebounceMs = 300;

    /// <summary>Raised when settings.json changes on disk.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Raised when spaces.json changes on disk.</summary>
    public event EventHandler? SpacesChanged;

    /// <summary>Raised when folder_portals.json changes on disk.</summary>
    public event EventHandler? PortalsChanged;

    /// <summary>Raised when sorting_rules.json changes on disk.</summary>
    public event EventHandler? SortingRulesChanged;

    public void Start()
    {
        if (_watcher != null) return;

        // Ensure directory exists so the watcher doesn't throw
        if (!Directory.Exists(WatchDir))
            Directory.CreateDirectory(WatchDir);

        _settingsDebounce = new Timer(_ => SettingsChanged?.Invoke(this, EventArgs.Empty));
        _spacesDebounce = new Timer(_ => SpacesChanged?.Invoke(this, EventArgs.Empty));
        _portalsDebounce = new Timer(_ => PortalsChanged?.Invoke(this, EventArgs.Empty));
        _sortingRulesDebounce = new Timer(_ => SortingRulesChanged?.Invoke(this, EventArgs.Empty));

        _watcher = new FileSystemWatcher(WatchDir, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        switch (e.Name?.ToLowerInvariant())
        {
            case "settings.json":
                _settingsDebounce?.Change(DebounceMs, Timeout.Infinite);
                break;
            case "spaces.json":
                _spacesDebounce?.Change(DebounceMs, Timeout.Infinite);
                break;
            case "folder_portals.json":
                _portalsDebounce?.Change(DebounceMs, Timeout.Infinite);
                break;
            case "sorting_rules.json":
                _sortingRulesDebounce?.Change(DebounceMs, Timeout.Infinite);
                break;
        }
    }

    public void Dispose()
    {
        Stop();
        _settingsDebounce?.Dispose();
        _spacesDebounce?.Dispose();
        _portalsDebounce?.Dispose();
        _sortingRulesDebounce?.Dispose();
    }
}
