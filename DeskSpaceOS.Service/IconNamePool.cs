using System;
using System.Collections.Generic;

namespace DeskSpaceOS.Service;

/// <summary>
/// Maps desktop icon display names to ListView indices. Each index can be taken once,
/// so duplicate names (e.g. "Report.docx" and "Report.pdf" with hidden extensions)
/// resolve to distinct icons instead of the same one.
/// </summary>
public sealed class IconNamePool
{
    private readonly Dictionary<string, Queue<int>> _byName = new(StringComparer.OrdinalIgnoreCase);

    public IconNamePool(IReadOnlyList<string> names, ICollection<int>? exclude = null)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i].Length == 0 || exclude?.Contains(i) == true) continue;
            if (!_byName.TryGetValue(names[i], out var queue))
                _byName[names[i]] = queue = new Queue<int>();
            queue.Enqueue(i);
        }
    }

    public bool TryTake(string name, out int index)
    {
        index = -1;
        return _byName.TryGetValue(name, out var queue) && queue.TryDequeue(out index);
    }
}
