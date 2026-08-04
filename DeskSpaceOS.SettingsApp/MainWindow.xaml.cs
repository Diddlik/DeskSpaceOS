using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;

        var width = Math.Min(1280, Math.Max(800, workArea.Width - 120));
        var height = Math.Min(820, Math.Max(600, workArea.Height - 120));
        appWindow.Resize(new SizeInt32(width, height));
        appWindow.Move(new PointInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2));

        NavView.SelectedItem = AboutNavItem;
        ContentFrame.Navigate(typeof(AboutPage));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            // Navigate to Settings
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "Spaces":
                    ContentFrame.Navigate(typeof(SpacesPage));
                    break;
                case "Portals":
                    ContentFrame.Navigate(typeof(FolderPortalsPage));
                    break;
                case "Appearance":
                    ContentFrame.Navigate(typeof(AppearancePage));
                    break;
                case "Rules":
                    ContentFrame.Navigate(typeof(SortingRulesPage));
                    break;
                case "Hotkeys":
                    ContentFrame.Navigate(typeof(HotkeysPage));
                    break;
                case "RollUp":
                    ContentFrame.Navigate(typeof(RollUpPage));
                    break;
                case "ZenMode":
                    ContentFrame.Navigate(typeof(ZenModePage));
                    break;
                case "Peek":
                    ContentFrame.Navigate(typeof(PeekPage));
                    break;
                case "QuickHide":
                    ContentFrame.Navigate(typeof(QuickHidePage));
                    break;
                case "Tabs":
                    ContentFrame.Navigate(typeof(TabsPage));
                    break;
                case "Layout":
                    ContentFrame.Navigate(typeof(LayoutPage));
                    break;
                case "About":
                    ContentFrame.Navigate(typeof(AboutPage));
                    break;
            }
        }
    }
}
