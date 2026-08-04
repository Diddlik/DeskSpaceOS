using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using DeskSpaceOS.Core.Storage;
using Windows.Globalization;

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
        AutoUpdateCheckToggle.IsOn = _settings.AutoUpdateCheck;
        LanguageComboBox.SelectedIndex = _settings.Language switch
        {
            "en-US" => 1,
            "de-DE" => 2,
            "ru-RU" => 3,
            "uk-UA" => 4,
            _ => 0
        };
        _loading = false;
    }

    private void EnableQuickHideToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings.EnableQuickHide = EnableQuickHideToggle.IsOn;
        AppSettingsStore.Save(_settings);
        ShowStatus(Loc.Get("Settings_QuickHideSaved"), InfoBarSeverity.Informational);
    }

    private void AutoUpdateCheckToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings.AutoUpdateCheck = AutoUpdateCheckToggle.IsOn;
        AppSettingsStore.Save(_settings);
        ShowStatus(Loc.Get("Settings_AutoUpdateCheckSaved"), InfoBarSeverity.Informational);
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
            ShowStatus(
                Loc.Get(enable ? "Settings_StartupEnabled" : "Settings_StartupDisabled"),
                InfoBarSeverity.Success);
        }
        catch (System.Exception ex)
        {
            ShowStatus(Loc.Format("Settings_StartupError", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void DisableAutoArrangeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings.DisableAutoArrange = DisableAutoArrangeToggle.IsOn;
        AppSettingsStore.Save(_settings);
        ShowStatus(Loc.Get("Settings_AutoArrangeSaved"), InfoBarSeverity.Informational);
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageComboBox.SelectedItem is not ComboBoxItem { Tag: string language })
            return;

        _settings.Language = language;
        AppSettingsStore.Save(_settings);
        ApplicationLanguages.PrimaryLanguageOverride = language;
        ShowStatus(Loc.Get("Settings_LanguageRestartRequired"), InfoBarSeverity.Informational);
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
