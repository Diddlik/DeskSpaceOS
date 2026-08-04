using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DeskSpaceOS.Core.Models;
using DeskSpaceOS.Core.Storage;
using Windows.UI;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class SpacesPage : Page
{
    private List<Space> _spaces = new();

    public SpacesPage()
    {
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        LoadSpaces();
    }

    private void LoadSpaces()
    {
        _spaces = SpaceStore.Load();
        RebuildList();
    }

    private void RebuildList()
    {
        SpaceList.ItemsSource = null;

        if (_spaces.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        var panels = new List<FrameworkElement>();
        foreach (var space in _spaces)
        {
            panels.Add(BuildSpaceCard(space));
        }
        SpaceList.ItemsSource = panels;
    }

    private FrameworkElement BuildSpaceCard(Space space)
    {
        var spaceColor = Windows.UI.Color.FromArgb(
            space.Alpha, space.ColorR, space.ColorG, space.ColorB);

        // --- Visual space preview ---
        var previewGrid = new Grid
        {
            Width = 280,
            MinHeight = 100,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // Space background
        var spaceBg = new Border
        {
            Background = new SolidColorBrush(spaceColor),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        // Space header
        var headerBar = new Border
        {
            Height = 28,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0, 0, 0)),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Child = new TextBlock
            {
                Text = space.Title,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        // Icon grid inside the space
        var iconWrap = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 32, 8, 8),
            Spacing = 2
        };

        // Wrap into rows
        var currentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        int col = 0;
        int maxCols = 4;

        var rowSpace = new StackPanel
        {
            Margin = new Thickness(8, 32, 8, 8),
            Spacing = 2
        };

        foreach (string iconName in space.IconNames)
        {
            if (col >= maxCols)
            {
                rowSpace.Children.Add(currentRow);
                currentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                col = 0;
            }

            var iconPanel = new StackPanel
            {
                Width = 62,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Pick glyph based on name/extension
            string glyph = GetIconGlyph(iconName);
            iconPanel.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 24,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            iconPanel.Children.Add(new TextBlock
            {
                Text = TruncateName(iconName, 10),
                FontSize = 9,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 62
            });

            currentRow.Children.Add(iconPanel);
            col++;
        }

        if (currentRow.Children.Count > 0)
            rowSpace.Children.Add(currentRow);

        if (space.IconNames.Count == 0)
        {
            rowSpace.Children.Add(new TextBlock
            {
                Text = "Empty",
                FontSize = 11,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 12)
            });
        }

        previewGrid.Children.Add(spaceBg);
        previewGrid.Children.Add(headerBar);
        previewGrid.Children.Add(rowSpace);

        // --- Info + buttons row ---
        var details = new TextBlock
        {
            Text = $"{space.IconNames.Count} icon(s) \u2022 {(int)space.Width}\u00d7{(int)space.Height}",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(0, 6, 0, 0)
        };

        // Edit button
        var editButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new FontIcon { Glyph = "\uE70F", FontSize = 14 }, new TextBlock { Text = "Edit" } }
            },
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Tag = space.Id,
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
                Children = { new FontIcon { Glyph = "\uE74D", FontSize = 14 }, new TextBlock { Text = "Delete" } }
            },
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Tag = space.Id,
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteButton.Click += DeleteButton_Click;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0)
        };
        buttonPanel.Children.Add(editButton);
        buttonPanel.Children.Add(deleteButton);

        // Assemble card
        var cardContent = new StackPanel { Spacing = 6 };
        cardContent.Children.Add(previewGrid);
        cardContent.Children.Add(details);
        cardContent.Children.Add(buttonPanel);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 4),
            Child = cardContent
        };
    }

    private static string GetIconGlyph(string name)
    {
        // Try to detect type from name
        string lower = name.ToLowerInvariant();

        if (lower.EndsWith(".lnk") || lower.EndsWith(".exe") || lower.EndsWith(".url"))
            return "\uE737"; // Application icon

        if (lower.EndsWith(".txt") || lower.EndsWith(".log") || lower.EndsWith(".md"))
            return "\uE8A5"; // Document

        if (lower.EndsWith(".jpg") || lower.EndsWith(".png") || lower.EndsWith(".bmp") ||
            lower.EndsWith(".gif") || lower.EndsWith(".ico") || lower.EndsWith(".svg"))
            return "\uEB9F"; // Photo

        if (lower.EndsWith(".mp3") || lower.EndsWith(".wav") || lower.EndsWith(".flac"))
            return "\uE8D6"; // Music

        if (lower.EndsWith(".mp4") || lower.EndsWith(".avi") || lower.EndsWith(".mkv"))
            return "\uE714"; // Video

        if (lower.EndsWith(".zip") || lower.EndsWith(".rar") || lower.EndsWith(".7z"))
            return "\uF012"; // Archive

        if (lower.EndsWith(".pdf"))
            return "\uEA90"; // PDF

        // No extension likely means a folder or shortcut
        if (!lower.Contains('.'))
            return "\uE8B7"; // Folder

        return "\uE7C3"; // Generic file
    }

    private static string TruncateName(string name, int maxLen)
    {
        // Strip common extensions for cleaner display
        string display = name;
        int dot = display.LastIndexOf('.');
        if (dot > 0)
        {
            string ext = display[dot..].ToLowerInvariant();
            if (ext is ".lnk" or ".url")
                display = display[..dot];
        }

        return display.Length <= maxLen ? display : display[..(maxLen - 1)] + "\u2026";
    }

    private async void CreateSpaceButton_Click(object sender, RoutedEventArgs e)
    {
        var titleBox = new TextBox
        {
            Text = GetDefaultSpaceTitle(),
            Header = "Title",
            PlaceholderText = "Space name"
        };

        var widthBox = new NumberBox
        {
            Header = "Width",
            Value = 320,
            Minimum = 160,
            Maximum = 1200,
            SmallChange = 10,
            LargeChange = 50,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var heightBox = new NumberBox
        {
            Header = "Height",
            Value = 220,
            Minimum = 120,
            Maximum = 900,
            SmallChange = 10,
            LargeChange = 50,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var sizeGrid = new Grid { ColumnSpacing = 12 };
        sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(widthBox, 0);
        Grid.SetColumn(heightBox, 1);
        sizeGrid.Children.Add(widthBox);
        sizeGrid.Children.Add(heightBox);

        var colorPicker = new ColorPicker
        {
            Color = GetDefaultSpaceColor(),
            IsAlphaEnabled = true,
            IsColorSpectrumVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true
        };

        var panel = new StackPanel { Spacing = 16, MinWidth = 360 };
        panel.Children.Add(titleBox);
        panel.Children.Add(sizeGrid);
        panel.Children.Add(new TextBlock { Text = "Color & Transparency" });
        panel.Children.Add(colorPicker);

        var dialog = new ContentDialog
        {
            Title = "Create Space",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        string title = string.IsNullOrWhiteSpace(titleBox.Text) ? GetDefaultSpaceTitle() : titleBox.Text.Trim();
        var color = colorPicker.Color;
        var space = new Space
        {
            Id = Guid.NewGuid(),
            Title = title,
            X = 120 + (_spaces.Count % 8) * 28,
            Y = 120 + (_spaces.Count % 8) * 28,
            Width = double.IsNaN(widthBox.Value) ? 320 : widthBox.Value,
            Height = double.IsNaN(heightBox.Value) ? 220 : heightBox.Value,
            ColorR = color.R,
            ColorG = color.G,
            ColorB = color.B,
            Alpha = color.A,
            Tabs = new List<SpaceTab> { new() { Name = title } },
            ActiveTabIndex = 0
        };

        _spaces.Add(space);
        SpaceStore.Save(_spaces);
        RebuildList();
        ShowStatus($"Created \"{space.Title}\". Changes applied automatically.");
    }

    private string GetDefaultSpaceTitle()
    {
        const string baseTitle = "New Space";
        if (!_spaces.Exists(c => string.Equals(c.Title, baseTitle, StringComparison.OrdinalIgnoreCase)))
            return baseTitle;

        int index = 2;
        while (_spaces.Exists(c => string.Equals(c.Title, $"{baseTitle} {index}", StringComparison.OrdinalIgnoreCase)))
            index++;

        return $"{baseTitle} {index}";
    }

    private static Windows.UI.Color GetDefaultSpaceColor()
    {
        var settings = AppSettingsStore.Load();
        return Windows.UI.Color.FromArgb(
            settings.DefaultAlpha,
            settings.DefaultColorR,
            settings.DefaultColorG,
            settings.DefaultColorB);
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Guid id) return;
        var space = _spaces.Find(c => c.Id == id);
        if (space == null) return;

        var titleBox = new TextBox { Text = space.Title, Header = "Title", PlaceholderText = "Space name" };

        var colorPicker = new ColorPicker
        {
            Color = Windows.UI.Color.FromArgb(space.Alpha, space.ColorR, space.ColorG, space.ColorB),
            IsAlphaEnabled = true,
            IsColorSpectrumVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true
        };

        var panel = new StackPanel { Spacing = 16, MinWidth = 320 };
        panel.Children.Add(titleBox);
        panel.Children.Add(new TextBlock { Text = "Color & Transparency" });
        panel.Children.Add(colorPicker);

        var dialog = new ContentDialog
        {
            Title = "Edit Space",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (!string.IsNullOrWhiteSpace(titleBox.Text))
                space.Title = titleBox.Text;

            var c = colorPicker.Color;
            space.ColorR = c.R;
            space.ColorG = c.G;
            space.ColorB = c.B;
            space.Alpha = c.A;

            SpaceStore.Save(_spaces);
            RebuildList();
            ShowStatus("Space updated. Changes applied automatically.");
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Guid id) return;
        var space = _spaces.Find(c => c.Id == id);
        if (space == null) return;

        var dialog = new ContentDialog
        {
            Title = "Delete Space",
            Content = $"Are you sure you want to delete \"{space.Title}\"? Icons will be released back to the desktop.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _spaces.Remove(space);
            SpaceStore.Save(_spaces);
            RebuildList();
            ShowStatus($"Deleted \"{space.Title}\". Changes applied automatically.");
        }
    }

    private void ShowStatus(string message)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.IsOpen = true;
    }
}
