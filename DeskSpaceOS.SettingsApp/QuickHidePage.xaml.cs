using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DeskSpaceOS.Core.Storage;
using DeskSpaceOS.Core.Models;
using System.Linq;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class QuickHidePage : Page
{
    private AppSettings _settings;
    private bool _isLoading = true;

    public QuickHidePage()
    {
        this.InitializeComponent();
        _settings = AppSettingsStore.Load();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;
        
        EnableQuickHideCheckBox.IsChecked = _settings.EnableQuickHide;
        AutoHideCheckBox.IsChecked = _settings.QuickHideAutoHide;
        AutoShowCheckBox.IsChecked = _settings.QuickHideAutoShow;
        ShowOnStartCheckBox.IsChecked = _settings.QuickHideShowOnStart;

        // Set ComboBox selection based on enum
        foreach (ComboBoxItem item in QuickHideScopeComboBox.Items)
        {
            if (item.Tag.ToString() == _settings.QuickHideScope.ToString())
            {
                QuickHideScopeComboBox.SelectedItem = item;
                break;
            }
        }

        _isLoading = false;
        SaveButton.IsEnabled = false;
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SaveButton.IsEnabled = true;
    }

    private void SettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        SaveButton.IsEnabled = true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.EnableQuickHide = EnableQuickHideCheckBox.IsChecked ?? false;
        _settings.QuickHideAutoHide = AutoHideCheckBox.IsChecked ?? false;
        _settings.QuickHideAutoShow = AutoShowCheckBox.IsChecked ?? false;
        _settings.QuickHideShowOnStart = ShowOnStartCheckBox.IsChecked ?? false;

        if (QuickHideScopeComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            if (System.Enum.TryParse<QuickHideScope>(selectedItem.Tag.ToString(), out var scope))
            {
                _settings.QuickHideScope = scope;
            }
        }

        AppSettingsStore.Save(_settings);
        SaveButton.IsEnabled = false;

        StatusInfoBar.Title = Loc.Get("QuickHide_SavedTitle");
        StatusInfoBar.Message = Loc.Get("QuickHide_SavedMessage");
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }
}
