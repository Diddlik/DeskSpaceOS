using System;
using System.Collections.Generic;

namespace DeskSpaceOS.Core.Models;

public class SpaceTab
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Tab 1";
    public List<string> IconNames { get; set; } = new List<string>();
}
