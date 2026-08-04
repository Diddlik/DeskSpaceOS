using System;
using System.Collections.Generic;
using System.IO;
using DeskSpaceOS.Service.Controls;

namespace DeskSpaceOS.Service;

/// <summary>
/// Manages FileSystemWatchers for all portal spaces.
/// Forwards file events to the corresponding PortalSpaceControl on the UI thread.
/// </summary>
public class FolderPortalWatcher : IDisposable
{
    private readonly Dictionary<Guid, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<Guid, PortalSpaceControl> _portals = new();

    /// <summary>
    /// Begin watching a directory for a portal space.
    /// The control's RefreshFiles() should be called before this to do the initial scan.
    /// </summary>
    public void Watch(PortalSpaceControl portal)
    {
        if (string.IsNullOrEmpty(portal.DirectoryPath) || !Directory.Exists(portal.DirectoryPath))
            return;

        if (_watchers.ContainsKey(portal.PortalId))
            return;

        _portals[portal.PortalId] = portal;

        var watcher = new FileSystemWatcher(portal.DirectoryPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        watcher.Created += (_, e) => OnCreated(portal.PortalId, e);
        watcher.Deleted += (_, e) => OnDeleted(portal.PortalId, e);
        watcher.Renamed += (_, e) => OnRenamed(portal.PortalId, e);

        _watchers[portal.PortalId] = watcher;
    }

    /// <summary>
    /// Re-registers the watcher for a portal (e.g. after tab switch changes the directory).
    /// </summary>
    public void Rewatch(PortalSpaceControl portal)
    {
        Unwatch(portal.PortalId);
        Watch(portal);
    }

    public void Unwatch(Guid portalId)
    {
        if (_watchers.TryGetValue(portalId, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _watchers.Remove(portalId);
        }
        _portals.Remove(portalId);
    }

    private void OnCreated(Guid portalId, FileSystemEventArgs e)
    {
        if (!_portals.TryGetValue(portalId, out var portal)) return;
        portal.Dispatcher.BeginInvoke(() => portal.AddFileEntry(e.FullPath));
    }

    private void OnDeleted(Guid portalId, FileSystemEventArgs e)
    {
        if (!_portals.TryGetValue(portalId, out var portal)) return;
        portal.Dispatcher.BeginInvoke(() => portal.RemoveFileEntry(e.FullPath));
    }

    private void OnRenamed(Guid portalId, RenamedEventArgs e)
    {
        if (!_portals.TryGetValue(portalId, out var portal)) return;
        portal.Dispatcher.BeginInvoke(() => portal.RenameFileEntry(e.OldFullPath, e.FullPath));
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        _portals.Clear();
    }
}
