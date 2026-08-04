using System;

namespace DeskSpaceOS.Core.Models;

/// <summary>What aspect of a desktop item the rule matches on.</summary>
public enum SortingRuleKind
{
    /// <summary>Match by file extension (uses <see cref="SortingRule.Pattern"/> or legacy <see cref="SortingRule.ExtensionPattern"/>).</summary>
    Extension,
    /// <summary>Match by broad file-type category (folders, documents, images, etc.).</summary>
    FileCategory,
    /// <summary>Match if the file name contains <see cref="SortingRule.Pattern"/> (case-insensitive).</summary>
    NameContains,
    /// <summary>Match shortcut (.lnk/.url) files whose target path contains <see cref="SortingRule.Pattern"/>.</summary>
    ShortcutTarget,
    /// <summary>Match by file age, bounded by <see cref="SortingRule.MinAgeDays"/>..<see cref="SortingRule.MaxAgeDays"/>.</summary>
    Age,
    /// <summary>Match by file size, bounded by <see cref="SortingRule.MinSizeBytes"/>..<see cref="SortingRule.MaxSizeBytes"/>.</summary>
    Size
}

/// <summary>Coarse file-type categories used by <see cref="SortingRuleKind.FileCategory"/>.</summary>
public enum FileCategory
{
    Folders,
    Documents,
    Images,
    Audio,
    Video,
    Archives,
    Executables,
    Shortcuts,
    Code,
    Other
}

public class SortingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- Legacy fields (still supported by the old settings UI) ---

    /// <summary>File extension pattern, e.g. ".jpg", ".png", ".pdf". Legacy — use <see cref="Pattern"/> for new rules.</summary>
    public string ExtensionPattern { get; set; } = string.Empty;

    /// <summary>Target space title to move matching files into. Used when <see cref="TargetSpaceId"/> is empty.</summary>
    public string TargetSpaceTitle { get; set; } = string.Empty;

    // --- New (Phase 5) ---

    /// <summary>Rule evaluation mode. Defaults to Extension for backward compatibility.</summary>
    public SortingRuleKind Kind { get; set; } = SortingRuleKind.Extension;

    /// <summary>
    /// General-purpose pattern. Meaning depends on <see cref="Kind"/>:
    /// Extension → ".png"; NameContains → substring; FileCategory → category name;
    /// ShortcutTarget → path substring.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    public FileCategory Category { get; set; } = FileCategory.Other;

    public long MinSizeBytes { get; set; } = 0;
    public long MaxSizeBytes { get; set; } = long.MaxValue;
    public int MinAgeDays { get; set; } = 0;
    public int MaxAgeDays { get; set; } = int.MaxValue;

    /// <summary>Preferred target (more stable than <see cref="TargetSpaceTitle"/>). Empty → fall back to title.</summary>
    public Guid TargetSpaceId { get; set; } = Guid.Empty;

    /// <summary>Lower is higher priority. Rules are evaluated in priority order.</summary>
    public int Priority { get; set; } = 100;

    public bool IsEnabled { get; set; } = true;
}
