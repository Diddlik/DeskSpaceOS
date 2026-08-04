using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DeskSpaceOS.Core.Storage;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class ZenModePage : Page
{
    private AppSettings _settings = new();
    private bool _loaded;

    public ZenModePage()
    {
        this.InitializeComponent();
    }

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
        
        string imageName = enabled ? "zen_mode_on.png" : "zen_mode_off.png";
        ZenModePreviewImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri($"ms-appx:///Assets/{imageName}"));
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
        StatusInfoBar.Message = "Zen Mode settings saved. Applied automatically.";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }
}
