using System;

namespace DeskSpaceOS.Core.Models;

public class DesktopIcon
{
    public int ListViewIndex { get; set; }
    
    // We should eventually store the name/path of the file to maintain 
    // persistence when the desktop icons are reordered or added/removed.
    public string Name { get; set; } = string.Empty;
    
    // Original coordinates before being moved into a space
    public int OriginalX { get; set; }
    public int OriginalY { get; set; }
}