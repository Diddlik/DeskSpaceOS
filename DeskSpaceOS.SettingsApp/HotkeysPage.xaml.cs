using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DeskSpaceOS.Core.Storage;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class HotkeysPage : Page
{
    private AppSettings _settings = new();

    public HotkeysPage()
    {
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = AppSettingsStore.Load();
        PeekHotkeyBox.Text = _settings.PeekModeHotkey;
        DistractionFreeHotkeyBox.Text = _settings.DistractionFreeHotkey;
        NewSpaceHotkeyBox.Text = _settings.NewSpaceHotkey;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.PeekModeHotkey = PeekHotkeyBox.Text.Trim();
        _settings.DistractionFreeHotkey = DistractionFreeHotkeyBox.Text.Trim();
        _settings.NewSpaceHotkey = NewSpaceHotkeyBox.Text.Trim();

        AppSettingsStore.Save(_settings);

        StatusInfoBar.Message = Loc.Get("Hotkeys_Saved");
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }
}
