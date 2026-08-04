using DeskSpaceOS.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class PeekPage : Page
{
    private AppSettings _settings = new();
    private bool _loaded;

    public PeekPage()
    {
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = AppSettingsStore.Load();
        EnablePeekToggle.IsOn = _settings.EnablePeekMode;
        PeekHotkeyBox.Text = _settings.PeekModeHotkey;
        PeekHotkeyBox.IsEnabled = _settings.EnablePeekMode;
        _loaded = true;
    }

    private void EnablePeekToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        _settings.EnablePeekMode = EnablePeekToggle.IsOn;
        PeekHotkeyBox.IsEnabled = EnablePeekToggle.IsOn;
        Persist();
    }

    private void PeekHotkeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;

        _settings.PeekModeHotkey = PeekHotkeyBox.Text.Trim();
        Persist();
    }

    private void Persist()
    {
        AppSettingsStore.Save(_settings);
        StatusInfoBar.Message = Loc.Get("Peek_Saved");
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }
}
