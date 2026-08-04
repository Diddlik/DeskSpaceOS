using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DeskSpaceOS.Core.Models;
using DeskSpaceOS.Core.Storage;
using Windows.UI;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class FolderPortalsPage : Page
{
    private const double PortalDialogWidth = 700;

    private List<FolderPortal> _portals = new();

    public FolderPortalsPage()
    {
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _portals = FolderPortalStore.Load();
        RebuildList();
    }

    private void RebuildList()
    {
        PortalList.ItemsSource = null;

        if (_portals.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        var cards = new List<FrameworkElement>();
        foreach (var portal in _portals)
            cards.Add(BuildPortalCard(portal));

        PortalList.ItemsSource = cards;
    }

    private FrameworkElement BuildPortalCard(FolderPortal portal)
    {
        // Folder icon
        var folderIcon = new FontIcon
        {
            Glyph = "\uE8B7",
            FontSize = 28,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF,
                portal.ColorR, portal.ColorG, portal.ColorB)),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Title
        var title = new TextBlock
        {
            Text = portal.Title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Top
        };

        // Path
        var pathText = new TextBlock
        {
            Text = portal.DirectoryPath,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 500
        };

        // View info
        string viewInfo = Loc.Get(portal.ViewMode == PortalViewMode.Icons
            ? "Portals_ViewIconsLabel"
            : "Portals_ViewDetailsLabel");
        string sortInfo = portal.SortColumn switch
        {
            PortalSortColumn.DateModified => Loc.Get("Portals_SortDateLabel"),
            PortalSortColumn.Size => Loc.Get("Portals_SortSizeLabel"),
            _ => Loc.Get("Portals_SortNameLabel")
        };
        sortInfo += portal.SortAscending ? " \u2191" : " \u2193";
        string navInfo = portal.EnableNavigation ? Loc.Get("Portals_NavigationOn") : "";

        var detailText = new TextBlock
        {
            Text = Loc.Format(
                "Portals_Details",
                (int)portal.Width,
                (int)portal.Height,
                viewInfo,
                sortInfo,
                navInfo),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        };

        var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(title);
        textStack.Children.Add(pathText);
        textStack.Children.Add(detailText);

        // Edit button
        var editButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new FontIcon { Glyph = "\uE70F", FontSize = 14 }, new TextBlock { Text = Loc.Get("Common_Edit") } }
            },
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Tag = portal.Id,
            VerticalAlignment = VerticalAlignment.Center
        };
        editButton.Click += EditButton_Click;

        // Delete button
        var deleteButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new FontIcon { Glyph = "\uE74D", FontSize = 14 }, new TextBlock { Text = Loc.Get("Common_Remove") } }
            },
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Tag = portal.Id,
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteButton.Click += DeleteButton_Click;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        buttonPanel.Children.Add(editButton);
        buttonPanel.Children.Add(deleteButton);

        var contentGrid = new Grid { ColumnSpacing = 12 };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(folderIcon, 0);
        Grid.SetColumn(textStack, 1);
        Grid.SetColumn(buttonPanel, 2);

        contentGrid.Children.Add(folderIcon);
        contentGrid.Children.Add(textStack);
        contentGrid.Children.Add(buttonPanel);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 4),
            Child = contentGrid
        };
    }

    private async void AddPortalButton_Click(object sender, RoutedEventArgs e)
    {
        var dialogContent = new FolderPortalDialogContent();
        var dialog = CreatePortalDialog(
            Loc.Get("Portals_AddDialogTitle"),
            Loc.Get("Common_Add"),
            dialogContent);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var data = dialogContent.GetData();
            if (string.IsNullOrWhiteSpace(data.DirectoryPath))
            {
                ShowStatus(Loc.Get("Portals_PathRequired"), InfoBarSeverity.Warning);
                return;
            }

            if (!System.IO.Directory.Exists(data.DirectoryPath))
            {
                ShowStatus(Loc.Get("Portals_DirectoryMissing"), InfoBarSeverity.Error);
                return;
            }

            _portals.Add(new FolderPortal
            {
                Title = GetPortalTitle(data),
                DirectoryPath = data.DirectoryPath,
                ViewMode = data.ViewMode,
                SortColumn = data.SortColumn,
                SortAscending = data.SortAscending,
                ShowNameColumn = true,
                ShowDateColumn = data.ShowDateColumn,
                ShowSizeColumn = data.ShowSizeColumn,
                EnableNavigation = data.EnableNavigation
            });

            FolderPortalStore.Save(_portals);
            RebuildList();
            ShowStatus(Loc.Get("Portals_Added"), InfoBarSeverity.Success);
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Guid id) return;
        var portal = _portals.Find(p => p.Id == id);
        if (portal == null) return;

        var dialogContent = new FolderPortalDialogContent();
        dialogContent.LoadFrom(portal);
        var dialog = CreatePortalDialog(
            Loc.Get("Portals_EditDialogTitle"),
            Loc.Get("Common_Save"),
            dialogContent);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var data = dialogContent.GetData();
            if (string.IsNullOrWhiteSpace(data.DirectoryPath))
            {
                ShowStatus(Loc.Get("Portals_PathRequired"), InfoBarSeverity.Warning);
                return;
            }

            if (!System.IO.Directory.Exists(data.DirectoryPath))
            {
                ShowStatus(Loc.Get("Portals_DirectoryMissing"), InfoBarSeverity.Error);
                return;
            }

            portal.Title = GetPortalTitle(data);
            portal.DirectoryPath = data.DirectoryPath;
            portal.ViewMode = data.ViewMode;
            portal.SortColumn = data.SortColumn;
            portal.SortAscending = data.SortAscending;
            portal.ShowNameColumn = true;
            portal.ShowDateColumn = data.ShowDateColumn;
            portal.ShowSizeColumn = data.ShowSizeColumn;
            portal.EnableNavigation = data.EnableNavigation;

            FolderPortalStore.Save(_portals);
            RebuildList();
            ShowStatus(Loc.Get("Portals_Updated"), InfoBarSeverity.Success);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
        {
            _portals.RemoveAll(p => p.Id == id);
            FolderPortalStore.Save(_portals);
            RebuildList();
            ShowStatus(Loc.Get("Portals_Removed"), InfoBarSeverity.Informational);
        }
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private ContentDialog CreatePortalDialog(string title, string primaryButtonText, FolderPortalDialogContent content)
    {
        return new ContentDialog
            {
            Title = title,
            Content = new ScrollViewer
            {
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MinHeight = 460
            },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            MinWidth = PortalDialogWidth,
            Width = PortalDialogWidth,
            MaxWidth = PortalDialogWidth
        };
    }

    private static string GetPortalTitle(FolderPortalDialogData data)
    {
        if (!string.IsNullOrWhiteSpace(data.Title))
            return data.Title;

        var trimmedPath = data.DirectoryPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var folderName = System.IO.Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(folderName) ? Loc.Get("Portals_DefaultName") : folderName;
    }
}
