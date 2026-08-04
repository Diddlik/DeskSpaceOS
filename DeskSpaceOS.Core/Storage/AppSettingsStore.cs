using System;
using System.IO;
using System.Text.Json;
using DeskSpaceOS.Core.Models;

namespace DeskSpaceOS.Core.Storage;

public class AppSettings
{
    public bool StartWithWindows { get; set; } = false;
    public bool DisableAutoArrange { get; set; } = true;

    // Default space appearance
    public byte DefaultColorR { get; set; } = 0x1A;
    public byte DefaultColorG { get; set; } = 0x1A;
    public byte DefaultColorB { get; set; } = 0x1A;
    public byte DefaultAlpha { get; set; } = 0x40;

    // Hotkeys (modifier+key format)
    public bool EnablePeekMode { get; set; } = true;
    public string PeekModeHotkey { get; set; } = "Win+Space";
    public string DistractionFreeHotkey { get; set; } = "Win+D";
    public string NewSpaceHotkey { get; set; } = "Ctrl+Shift+N";

    public TabStyle TabStyle { get; set; } = TabStyle.Rounded;
    public bool ZenModeEnabled { get; set; } = false;

    /// <summary>Seconds of mouse inactivity over non-desktop windows before spaces fade.</summary>
    public double ZenModeIdleSeconds { get; set; } = 1.5;

    /// <summary>Opacity of spaces when Zen Mode is faded (0.0–1.0).</summary>
    public double ZenModeFadedOpacity { get; set; } = 0.15;

    public HeaderVisibility HeaderVisibility { get; set; } = HeaderVisibility.Always;
    public bool HideScrollbarsDuringInactivity { get; set; } = true;
    public bool AnimateIconsOnHover { get; set; } = true;
    public bool EnableRollUp { get; set; } = true;
    public bool EnableQuickHide { get; set; } = true;
    public QuickHideScope QuickHideScope { get; set; } = QuickHideScope.IconsAndSpaces;
    public bool QuickHideAutoHide { get; set; } = false;
    public bool QuickHideAutoShow { get; set; } = false;
    public bool QuickHideShowOnStart { get; set; } = true;

    /// <summary>Whether spaces and portals snap to screen edges and each other while dragging.</summary>
    public bool SnappingEnabled { get; set; } = true;

    /// <summary>Snap threshold in DIPs — distance at which drag coordinates pull to a snap target.</summary>
    public double SnapThresholdDIPs { get; set; } = 10.0;
}

public static class AppSettingsStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskSpaceOS",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        if (!File.Exists(StorePath))
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        string? dir = Path.GetDirectoryName(StorePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(StorePath, json);
    }
}
