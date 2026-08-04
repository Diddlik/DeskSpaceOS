using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DeskSpaceOS.Core.Storage;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class LayoutPage : Page
{
    private AppSettings _settings = new();
    private bool _loaded;

    public LayoutPage()
    {
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = AppSettingsStore.Load();
        SnappingToggle.IsOn = _settings.SnappingEnabled;
        ThresholdBox.Value = _settings.SnapThresholdDIPs;
        ThresholdBox.IsEnabled = _settings.SnappingEnabled;
        _loaded = true;
    }

    private void SnappingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _settings.SnappingEnabled = SnappingToggle.IsOn;
        ThresholdBox.IsEnabled = SnappingToggle.IsOn;
        Persist();
    }

    private void Threshold_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loaded) return;
        if (double.IsNaN(args.NewValue)) return;
        _settings.SnapThresholdDIPs = args.NewValue;
        Persist();
    }

    private void Persist()
    {
        AppSettingsStore.Save(_settings);
        StatusInfoBar.Message = Loc.Get("Layout_Saved");
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }
}
