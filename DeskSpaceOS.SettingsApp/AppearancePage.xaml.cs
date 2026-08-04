using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DeskSpaceOS.Core.Storage;
using Windows.UI;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class AppearancePage : Page
{
    private AppSettings _settings = new();

    public AppearancePage()
    {
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = AppSettingsStore.Load();
        DefaultColorPicker.Color = Color.FromArgb(
            _settings.DefaultAlpha, _settings.DefaultColorR, _settings.DefaultColorG, _settings.DefaultColorB);
        UpdatePreview();
    }

    private void DefaultColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var c = DefaultColorPicker.Color;
        PreviewBorder.Background = new SolidColorBrush(c);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var c = DefaultColorPicker.Color;
        _settings.DefaultColorR = c.R;
        _settings.DefaultColorG = c.G;
        _settings.DefaultColorB = c.B;
        _settings.DefaultAlpha = c.A;

        AppSettingsStore.Save(_settings);

        StatusInfoBar.Message = Loc.Get("Appearance_Saved");
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }
}
