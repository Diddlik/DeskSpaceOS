using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using DeskSpaceOS.Core.Storage;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class SettingsPage : Page
{
    private AppSettings _settings = new();
    private bool _loading = true;

    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DeskSpaceOS";

    public SettingsPage()
    {
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        _settings = AppSettingsStore.Load();

        // Check actual registry state for startup
        StartWithWindowsToggle.IsOn = IsStartupEnabled();
        DisableAutoArrangeToggle.IsOn = _settings.DisableAutoArrange;
        EnableQuickHideToggle.IsOn = _settings.EnableQuickHide;
        _loading = false;
    }

    private void EnableQuickHideToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings.EnableQuickHide = EnableQuickHideToggle.IsOn;
        AppSettingsStore.Save(_settings);
        ShowStatus("Quick Hide setting saved.", InfoBarSeverity.Informational);
    }

    private void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enable = StartWithWindowsToggle.IsOn;
        _settings.StartWithWindows = enable;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key != null)
            {
                if (enable)
                {
                    string fullPath = FindServiceExePath();
                    key.SetValue(AppName, $"\"{fullPath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }

            AppSettingsStore.Save(_settings);
            ShowStatus(enable ? "DeskSpace OS will start with Windows." : "Startup entry removed.", InfoBarSeverity.Success);
        }
        catch (System.Exception ex)
        {
            ShowStatus($"Could not update startup setting: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void DisableAutoArrangeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings.DisableAutoArrange = DisableAutoArrangeToggle.IsOn;
        AppSettingsStore.Save(_settings);
        ShowStatus("Setting saved. Applied automatically.", InfoBarSeverity.Informational);
    }

    private static string FindServiceExePath()
    {
        // Velopack install: %LocalAppData%\DeskSpaceOS\current\DeskSpaceOS.Service.exe
        string velopackPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskSpaceOS", "current", "DeskSpaceOS.Service.exe");
        if (System.IO.File.Exists(velopackPath))
            return velopackPath;

        // Dev-time fallback: sibling project output directory
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.AppDomain.CurrentDomain.BaseDirectory,
            "..", "DeskSpaceOS.Service", "DeskSpaceOS.Service.exe"));
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }
}
