using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DeskSpaceOS.Core.Models;

namespace DeskSpaceOS.Core.Storage;

public static class SortingRuleStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskSpaceOS",
        "sorting_rules.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static List<SortingRule> Load()
    {
        if (!File.Exists(StorePath))
            return new List<SortingRule>();

        try
        {
            string json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<SortingRule>>(json, JsonOptions)
                   ?? new List<SortingRule>();
        }
        catch
        {
            return new List<SortingRule>();
        }
    }

    public static void Save(List<SortingRule> rules)
    {
        string? dir = Path.GetDirectoryName(StorePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(rules, JsonOptions);
        File.WriteAllText(StorePath, json);
    }
}
