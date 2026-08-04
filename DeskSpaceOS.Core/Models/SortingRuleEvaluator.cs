using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeskSpaceOS.Core.Models;

/// <summary>Evaluates <see cref="SortingRule"/> objects against concrete desktop files.</summary>
public static class SortingRuleEvaluator
{
    /// <summary>Extensions considered to fall into each <see cref="FileCategory"/>.</summary>
    private static readonly Dictionary<FileCategory, HashSet<string>> CategoryExtensions = new()
    {
        [FileCategory.Documents] = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".doc", ".docx", ".rtf", ".odt", ".pdf", ".md", ".csv", ".xls", ".xlsx", ".ppt", ".pptx", ".odp", ".ods" },
        [FileCategory.Images] = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".svg", ".ico", ".webp", ".tiff", ".tif" },
        [FileCategory.Audio] = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" },
        [FileCategory.Video] = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".flv", ".m4v" },
        [FileCategory.Archives] = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz" },
        [FileCategory.Executables] = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".bat", ".cmd", ".com", ".ps1", ".sh" },
        [FileCategory.Shortcuts] = new(StringComparer.OrdinalIgnoreCase) { ".lnk", ".url" },
        [FileCategory.Code] = new(StringComparer.OrdinalIgnoreCase) { ".cs", ".cpp", ".c", ".h", ".hpp", ".py", ".js", ".ts", ".json", ".xml", ".yaml", ".yml", ".go", ".rs", ".java", ".rb", ".php" }
    };

    /// <summary>
    /// Iterate the enabled rules in priority order and return the first one that matches
    /// the given file, or null if none matched.
    /// </summary>
    public static SortingRule? FindMatch(IEnumerable<SortingRule> rules, string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        bool isDirectory = Directory.Exists(filePath);
        FileInfo? fi = null;
        if (!isDirectory)
        {
            try { fi = new FileInfo(filePath); } catch { return null; }
            if (fi == null || !fi.Exists) return null;
        }

        foreach (var rule in rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority))
        {
            if (Matches(rule, filePath, isDirectory, fi))
                return rule;
        }
        return null;
    }

    private static bool Matches(SortingRule rule, string filePath, bool isDirectory, FileInfo? fi)
    {
        string name = Path.GetFileName(filePath);
        string ext = Path.GetExtension(filePath);

        switch (rule.Kind)
        {
            case SortingRuleKind.Extension:
            {
                // Support the legacy field too (e.g. ".jpg,.png" or a single ".pdf").
                string pattern = !string.IsNullOrEmpty(rule.Pattern) ? rule.Pattern : rule.ExtensionPattern;
                if (string.IsNullOrEmpty(pattern) || isDirectory) return false;
                foreach (var token in pattern.Split(',', ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = token.StartsWith('.') ? token : "." + token;
                    if (string.Equals(t, ext, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }

            case SortingRuleKind.FileCategory:
                return MatchesCategory(rule.Category, isDirectory, ext);

            case SortingRuleKind.NameContains:
                return !string.IsNullOrEmpty(rule.Pattern)
                       && name.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase);

            case SortingRuleKind.ShortcutTarget:
            {
                if (string.IsNullOrEmpty(rule.Pattern)) return false;
                if (!string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase)) return false;
                string? target = TryReadShortcutTarget(filePath);
                return target != null && target.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase);
            }

            case SortingRuleKind.Age:
            {
                if (fi == null) return false;
                int age = (int)(DateTime.Now - fi.LastWriteTime).TotalDays;
                return age >= rule.MinAgeDays && age <= rule.MaxAgeDays;
            }

            case SortingRuleKind.Size:
            {
                if (fi == null) return false;
                return fi.Length >= rule.MinSizeBytes && fi.Length <= rule.MaxSizeBytes;
            }
        }

        return false;
    }

    private static bool MatchesCategory(FileCategory category, bool isDirectory, string ext)
    {
        if (category == FileCategory.Folders) return isDirectory;
        if (isDirectory) return false;

        if (CategoryExtensions.TryGetValue(category, out var set))
            return set.Contains(ext);

        if (category == FileCategory.Other)
        {
            // "Other" = not in any other known category
            foreach (var kv in CategoryExtensions)
                if (kv.Value.Contains(ext)) return false;
            return true;
        }
        return false;
    }

    /// <summary>Best-effort read of a Windows .lnk/.url target without COM dependencies.</summary>
    private static string? TryReadShortcutTarget(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        try
        {
            if (string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(4).Trim();
                }
            }
            else if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                // Light-weight .lnk parse: scan for any ASCII path fragment. Good enough for rule matching.
                byte[] bytes = File.ReadAllBytes(filePath);
                string ascii = System.Text.Encoding.ASCII.GetString(bytes);
                int idx = ascii.IndexOf(":\\", StringComparison.Ordinal);
                if (idx > 0)
                {
                    int start = idx - 1;
                    int end = idx + 2;
                    while (end < ascii.Length && ascii[end] >= 32 && ascii[end] < 127 && ascii[end] != 0) end++;
                    return ascii.Substring(start, end - start);
                }
            }
        }
        catch { }
        return null;
    }
}
