using System;
using System.Collections.Generic;

namespace DeskSpaceOS.Core.Models;

public enum PortalViewMode
{
    Icons,
    Details
}

public enum PortalSortColumn
{
    Name,
    DateModified,
    Size
}

public class FolderPortal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Portal";
    public string DirectoryPath { get; set; } = string.Empty;
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 280;

    public bool IsRolledUp { get; set; } = false;
    public double ExpandedHeight { get; set; } = 280;

    public byte ColorR { get; set; } = 0x00;
    public byte ColorG { get; set; } = 0x30;
    public byte ColorB { get; set; } = 0x50;
    public byte Alpha { get; set; } = 0x60;

    // View settings
    public PortalViewMode ViewMode { get; set; } = PortalViewMode.Icons;
    public bool ShowNameColumn { get; set; } = true;
    public bool ShowDateColumn { get; set; } = true;
    public bool ShowSizeColumn { get; set; } = true;
    public PortalSortColumn SortColumn { get; set; } = PortalSortColumn.Name;
    public bool SortAscending { get; set; } = true;

    // Multi-tab support
    // Legacy: DirectoryPath is used when Tabs is empty (pre-tab data). Migrated to single tab on load.
    public List<PortalTab> Tabs { get; set; } = new List<PortalTab>();
    public int ActiveTabIndex { get; set; } = 0;

    /// <summary>
    /// If true, double-clicking a folder inside the portal navigates into it
    /// instead of opening Windows Explorer.
    /// </summary>
    public bool EnableNavigation { get; set; } = true;
}
