using System;
using System.Collections.Generic;

namespace DeskSpaceOS.Core.Models;

public class Space
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New Space";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 200;

    // Color stored as RGB bytes + alpha byte
    public byte ColorR { get; set; } = 0x1A;
    public byte ColorG { get; set; } = 0x1A;
    public byte ColorB { get; set; } = 0x1A;
    public byte Alpha { get; set; } = 0x40;

    public bool IsRolledUp { get; set; } = false;
    public double ExpandedHeight { get; set; } = 200;

    // Icon names for persistence (indices change between sessions)
    // Legacy: used when Tabs is empty (pre-tab data). Migrated to single tab on load.
    public List<string> IconNames { get; set; } = new List<string>();

    // Multi-tab support
    public List<SpaceTab> Tabs { get; set; } = new List<SpaceTab>();
    public int ActiveTabIndex { get; set; } = 0;
}
