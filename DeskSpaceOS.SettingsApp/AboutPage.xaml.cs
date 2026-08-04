using Microsoft.UI.Xaml.Controls;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class AboutPage : Page
{
    public string AppVersion => Loc.Format("About_Version", UpdateChecker.CurrentVersion);

    public AboutPage()
    {
        this.InitializeComponent();
    }
}