using System;

namespace DeskSpaceOS.Core.Models;

public class PortalTab
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Tab 1";

    /// <summary>Home folder the tab was created from (used by the Home button to reset navigation).</summary>
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>Current navigated path (equal to DirectoryPath when not navigated deeper). Optional for backward compat.</summary>
    public string CurrentPath { get; set; } = string.Empty;
}
