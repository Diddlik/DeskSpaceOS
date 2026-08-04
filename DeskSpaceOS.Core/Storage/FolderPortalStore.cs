using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DeskSpaceOS.Core.Models;

namespace DeskSpaceOS.Core.Storage;

public static class FolderPortalStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskSpaceOS",
        "folder_portals.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static List<FolderPortal> Load()
    {
        if (!File.Exists(StorePath))
            return new List<FolderPortal>();

        try
        {
            string json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<FolderPortal>>(json, JsonOptions)
                   ?? new List<FolderPortal>();
        }
        catch
        {
            return new List<FolderPortal>();
        }
    }

    public static void Save(List<FolderPortal> portals)
    {
        string? dir = Path.GetDirectoryName(StorePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(portals, JsonOptions);
        File.WriteAllText(StorePath, json);
    }
}
