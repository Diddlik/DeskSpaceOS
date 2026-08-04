using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using DeskSpaceOS.Core.Storage;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class ZenModePage : Page
{
    private AppSettings _settings = new();
    private bool _loaded;

    public ZenModePage()
    {
        this.InitializeComponent();

        // Unpackaged WinUI 3 fails to load large images through ms-appx/relative
        // URIs (they resolve to loose files via StorageFile, which needs package
        // identity). Load the previews straight from the exe directory instead.
        PreviewOffImage.Source = LoadAsset("zen_mode_off.png");
        PreviewOnImage.Source = LoadAsset("zen_mode_on.png");
        AutomationProperties.SetName(PreviewOffImage, Loc.Get("Zen_PreviewOffImageName"));
        AutomationProperties.SetName(PreviewOnImage, Loc.Get("Zen_PreviewOnImageName"));
    }

    private static BitmapImage LoadAsset(string fileName) =>
        new(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", fileName)));

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = AppSettingsStore.Load();
        ZenModeToggle.IsOn = _settings.ZenModeEnabled;
        IdleBox.Value = _settings.ZenModeIdleSeconds;
        OpacityBox.Value = _settings.ZenModeFadedOpacity;
        ApplyTuningEnabled(_settings.ZenModeEnabled);
        _loaded = true;
    }

    private void ApplyTuningEnabled(bool enabled)
    {
        IdleBox.IsEnabled = enabled;
        OpacityBox.IsEnabled = enabled;
        HighlightActivePreview(enabled);
    }

    // Indicate the active state two ways so it isn't visual-only: a visible "Active"
    // text label (announced by screen readers) plus dimming the inactive card.
    private void HighlightActivePreview(bool zenOn)
    {
        PreviewOnActiveLabel.Visibility = zenOn ? Visibility.Visible : Visibility.Collapsed;
        PreviewOffActiveLabel.Visibility = zenOn ? Visibility.Collapsed : Visibility.Visible;
        PreviewOnCard.Opacity = zenOn ? 1.0 : 0.45;
        PreviewOffCard.Opacity = zenOn ? 0.45 : 1.0;
    }

    private void ZenModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _settings.ZenModeEnabled = ZenModeToggle.IsOn;
        ApplyTuningEnabled(ZenModeToggle.IsOn);
        Persist();
    }

    private void AnyValue_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loaded) return;
        if (double.IsNaN(args.NewValue)) return;

        _settings.ZenModeIdleSeconds = IdleBox.Value;
        _settings.ZenModeFadedOpacity = OpacityBox.Value;
        Persist();
    }

    private void Persist()
    {
        AppSettingsStore.Save(_settings);
        StatusInfoBar.Message = Loc.Get("Zen_Saved");
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }
}
