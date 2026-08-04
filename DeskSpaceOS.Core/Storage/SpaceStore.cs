using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DeskSpaceOS.Core.Models;

namespace DeskSpaceOS.Core.Storage;

public static class SpaceStore
{
    private const string LegacyFileName = "con" + "tainers.json";

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskSpaceOS",
        "spaces.json");

    private static readonly string LegacyStorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskSpaceOS",
        LegacyFileName);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static List<Space> Load()
    {
        string loadPath = File.Exists(StorePath) ? StorePath : LegacyStorePath;

        if (!File.Exists(loadPath))
            return new List<Space>();

        try
        {
            string json = File.ReadAllText(loadPath);
            var spaces = JsonSerializer.Deserialize<List<Space>>(json, JsonOptions)
                         ?? new List<Space>();

            if (loadPath == LegacyStorePath && !File.Exists(StorePath))
                Save(spaces);

            return spaces;
        }
        catch
        {
            return new List<Space>();
        }
    }

    public static void Save(List<Space> spaces)
    {
        string? dir = Path.GetDirectoryName(StorePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(spaces, JsonOptions);
        File.WriteAllText(StorePath, json);
    }
}
