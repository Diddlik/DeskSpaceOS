using System;
using System.Collections.Generic;
using System.IO;

namespace DeskSpaceOS.Service;

/// <summary>
/// Watches the user and public desktop folders for new files and fires events
/// the UI layer can use to evaluate sorting rules.
/// </summary>
public class DesktopFileMonitor : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();

    /// <summary>Raised (on a worker thread) when a file is created or moved into a desktop folder.</summary>
    public event EventHandler<string>? FileArrived;

    public void Start()
    {
        string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        foreach (var dir in new[] { userDesktop, publicDesktop })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

            var w = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            w.Created += (_, e) => FileArrived?.Invoke(this, e.FullPath);
            w.Renamed += (_, e) => FileArrived?.Invoke(this, e.FullPath);
            _watchers.Add(w);
        }
    }

    public void Dispose()
    {
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }
}
