using DeskSpaceOS.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class RollUpPage : Page
{
    private AppSettings _settings = new();
    private bool _loaded;

    public RollUpPage()
    {
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = AppSettingsStore.Load();
        EnableRollUpToggle.IsOn = _settings.EnableRollUp;
        _loaded = true;
    }

    private void EnableRollUpToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        _settings.EnableRollUp = EnableRollUpToggle.IsOn;
        AppSettingsStore.Save(_settings);

        StatusInfoBar.Message = Loc.Get("RollUp_Saved");
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }
}
