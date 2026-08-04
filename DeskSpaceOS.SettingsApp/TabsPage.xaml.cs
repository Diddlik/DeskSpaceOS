using System;
using DeskSpaceOS.Core.Models;
using DeskSpaceOS.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class TabsPage : Page
{
    private AppSettings _settings = new();
    private bool _loaded;

    public TabsPage()
    {
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = AppSettingsStore.Load();
        SelectTabStyle(_settings.TabStyle);
        _loaded = true;
    }

    private void TabStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || TabStyleComboBox.SelectedItem is not ComboBoxItem selectedItem)
            return;

        if (selectedItem.Tag is not string tag || !Enum.TryParse<TabStyle>(tag, out var tabStyle))
            return;

        _settings.TabStyle = tabStyle;
        AppSettingsStore.Save(_settings);

        StatusInfoBar.Message = "Tab settings saved. Applied automatically.";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }

    private void SelectTabStyle(TabStyle tabStyle)
    {
        foreach (ComboBoxItem item in TabStyleComboBox.Items)
        {
            if (item.Tag is string tag && string.Equals(tag, tabStyle.ToString(), StringComparison.Ordinal))
            {
                TabStyleComboBox.SelectedItem = item;
                break;
            }
        }
    }
}
