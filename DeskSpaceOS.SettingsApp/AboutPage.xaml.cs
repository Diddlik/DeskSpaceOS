using Microsoft.UI.Xaml.Controls;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class AboutPage : Page
{
    public string AppVersion
    {
        get
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return version != null ? $"Version {version.Major}.{version.Minor}.{version.Build}" : "Version Unknown";
        }
    }

    public AboutPage()
    {
        this.InitializeComponent();
    }
}