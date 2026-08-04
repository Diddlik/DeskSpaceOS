using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DeskSpaceOS.Core.Models;
using DeskSpaceOS.Core.Storage;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class SortingRulesPage : Page
{
    private List<SortingRule> _rules = new();
    private List<Space> _spaces = new();

    public SortingRulesPage()
    {
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _rules = SortingRuleStore.Load();
        _spaces = SpaceStore.Load();
        RebuildList();
    }

    private void RebuildList()
    {
        RulesList.ItemsSource = null;

        if (_rules.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        _rules.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        var cards = new List<FrameworkElement>();
        foreach (var rule in _rules)
            cards.Add(BuildRuleCard(rule));

        RulesList.ItemsSource = cards;
    }

    private FrameworkElement BuildRuleCard(SortingRule rule)
    {
        var enableToggle = new ToggleSwitch
        {
            IsOn = rule.IsEnabled,
            OnContent = "",
            OffContent = "",
            Tag = rule.Id,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0
        };
        AutomationProperties.SetName(enableToggle, Loc.Get("Rules_EnableRule"));
        enableToggle.Toggled += (s, _) =>
        {
            if (s is ToggleSwitch ts && ts.Tag is Guid id)
            {
                var r = _rules.Find(x => x.Id == id);
                if (r != null)
                {
                    r.IsEnabled = ts.IsOn;
                    SortingRuleStore.Save(_rules);
                }
            }
        };

        var priorityText = new TextBlock
        {
            Text = $"#{rule.Priority}",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 32
        };

        var kindText = new TextBlock
        {
            Text = DescribeKind(rule),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var patternText = new TextBlock
        {
            Text = DescribePattern(rule),
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 280
        };

        var arrowText = new FontIcon
        {
            Glyph = "\uE72A",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        var targetText = new TextBlock
        {
            Text = ResolveTargetTitle(rule),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        var editButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 14 },
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Tag = rule.Id,
            VerticalAlignment = VerticalAlignment.Center
        };
        editButton.Click += EditRule_Click;
        AutomationProperties.SetName(editButton, Loc.Get("Common_Edit"));

        var deleteButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Tag = rule.Id,
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteButton.Click += DeleteRule_Click;
        AutomationProperties.SetName(deleteButton, Loc.Get("Common_Delete"));

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(enableToggle);
        row.Children.Add(priorityText);
        row.Children.Add(kindText);
        row.Children.Add(patternText);
        row.Children.Add(arrowText);
        row.Children.Add(targetText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        buttons.Children.Add(editButton);
        buttons.Children.Add(deleteButton);

        var contentGrid = new Grid { ColumnSpacing = 12 };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(row, 0);
        Grid.SetColumn(buttons, 1);
        contentGrid.Children.Add(row);
        contentGrid.Children.Add(buttons);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 4),
            Child = contentGrid
        };
    }

    private static string DescribeKind(SortingRule rule) => GetKindLabel(rule.Kind);

    private static string GetKindLabel(SortingRuleKind kind) => kind switch
    {
        SortingRuleKind.Extension => Loc.Get("Rules_KindExtension"),
        SortingRuleKind.FileCategory => Loc.Get("Rules_KindCategory"),
        SortingRuleKind.NameContains => Loc.Get("Rules_KindName"),
        SortingRuleKind.ShortcutTarget => Loc.Get("Rules_KindShortcut"),
        SortingRuleKind.Age => Loc.Get("Rules_KindAge"),
        SortingRuleKind.Size => Loc.Get("Rules_KindSize"),
        _ => kind.ToString()
    };

    private static string GetCategoryLabel(FileCategory category) =>
        Loc.Get($"Rules_Category{category}");

    private static string DescribePattern(SortingRule r) => r.Kind switch
    {
        SortingRuleKind.Extension => !string.IsNullOrEmpty(r.Pattern) ? r.Pattern : r.ExtensionPattern,
        SortingRuleKind.FileCategory => GetCategoryLabel(r.Category),
        SortingRuleKind.NameContains => $"\u201c{r.Pattern}\u201d",
        SortingRuleKind.ShortcutTarget => $"\u201c{r.Pattern}\u201d",
        SortingRuleKind.Age => FormatAgeRange(r.MinAgeDays, r.MaxAgeDays),
        SortingRuleKind.Size => FormatSizeRange(r.MinSizeBytes, r.MaxSizeBytes),
        _ => string.Empty
    };

    private static string FormatAgeRange(int min, int max)
    {
        string hi = max == int.MaxValue ? "\u221E" : max.ToString(CultureInfo.CurrentCulture);
        return Loc.Format("Rules_AgeRange", min, hi);
    }

    private static string FormatSizeRange(long min, long max)
    {
        string hi = max == long.MaxValue ? "\u221E" : FormatBytes(max);
        return $"{FormatBytes(min)}..{hi}";
    }

    private static string FormatBytes(long v)
    {
        if (v <= 0) return "0 B";
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double d = v; int i = 0;
        while (d >= 1024 && i < u.Length - 1) { d /= 1024; i++; }
        return $"{d:0.##} {u[i]}";
    }

    private string ResolveTargetTitle(SortingRule r)
    {
        if (r.TargetSpaceId != Guid.Empty)
        {
            var c = _spaces.Find(x => x.Id == r.TargetSpaceId);
            if (c != null) return c.Title;
        }
        return string.IsNullOrEmpty(r.TargetSpaceTitle) ? Loc.Get("Rules_NoTarget") : r.TargetSpaceTitle;
    }

    private async void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowRuleDialog(null);
    }

    private async void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
        {
            var rule = _rules.Find(r => r.Id == id);
            if (rule != null) await ShowRuleDialog(rule);
        }
    }

    private async System.Threading.Tasks.Task ShowRuleDialog(SortingRule? existing)
    {
        bool isEdit = existing != null;
        var rule = existing ?? new SortingRule { Kind = SortingRuleKind.Extension, Priority = NextPriority() };

        var kindCombo = new ComboBox
        {
            Header = Loc.Get("Rules_MatchBy"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var kind in Enum.GetValues<SortingRuleKind>())
            kindCombo.Items.Add(GetKindLabel(kind));
        kindCombo.SelectedIndex = (int)rule.Kind;

        // --- Inputs per kind (switched by visibility) ---
        var extensionBox = new TextBox
        {
            Header = Loc.Get("Rules_Extensions"),
            PlaceholderText = ".jpg, .png, .pdf",
            Text = !string.IsNullOrEmpty(rule.Pattern) ? rule.Pattern : rule.ExtensionPattern
        };

        var categoryCombo = new ComboBox
        {
            Header = Loc.Get("Rules_FileCategory"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var category in Enum.GetValues<FileCategory>())
            categoryCombo.Items.Add(GetCategoryLabel(category));
        categoryCombo.SelectedIndex = (int)rule.Category;

        var nameBox = new TextBox
        {
            Header = Loc.Get("Rules_NameContains"),
            PlaceholderText = Loc.Get("Rules_NameExample"),
            Text = rule.Kind == SortingRuleKind.NameContains ? rule.Pattern : string.Empty
        };

        var shortcutBox = new TextBox
        {
            Header = Loc.Get("Rules_ShortcutTarget"),
            PlaceholderText = @"C:\Program Files\ or notepad.exe",
            Text = rule.Kind == SortingRuleKind.ShortcutTarget ? rule.Pattern : string.Empty
        };

        var ageMin = new NumberBox
        {
            Header = Loc.Get("Rules_MinAge"),
            Minimum = 0,
            Maximum = 100000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Value = rule.MinAgeDays
        };
        var ageMax = new NumberBox
        {
            Header = Loc.Get("Rules_MaxAge"),
            Minimum = -1,
            Maximum = 100000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Value = rule.MaxAgeDays == int.MaxValue ? -1 : rule.MaxAgeDays
        };

        var sizeMin = new NumberBox
        {
            Header = Loc.Get("Rules_MinSize"),
            Minimum = 0,
            Maximum = 1024L * 1024L * 1024L,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Value = rule.MinSizeBytes / 1024.0
        };
        var sizeMax = new NumberBox
        {
            Header = Loc.Get("Rules_MaxSize"),
            Minimum = -1,
            Maximum = 1024L * 1024L * 1024L,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Value = rule.MaxSizeBytes == long.MaxValue ? -1 : rule.MaxSizeBytes / 1024.0
        };

        // Space ordering
        var targetCombo = new ComboBox
        {
            Header = Loc.Get("Rules_TargetSpace"),
            PlaceholderText = Loc.Get("Rules_TargetSpacePlaceholder"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        int targetIndex = -1;
        for (int i = 0; i < _spaces.Count; i++)
        {
            var c = _spaces[i];
            targetCombo.Items.Add(c.Title);
            if (c.Id == rule.TargetSpaceId ||
                (rule.TargetSpaceId == Guid.Empty &&
                 string.Equals(c.Title, rule.TargetSpaceTitle, StringComparison.Ordinal)))
                targetIndex = i;
        }
        if (targetIndex >= 0) targetCombo.SelectedIndex = targetIndex;

        var priorityBox = new NumberBox
        {
            Header = Loc.Get("Rules_Priority"),
            Minimum = 0,
            Maximum = 10000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Value = rule.Priority
        };

        // Group inputs per kind
        var extPanel = new StackPanel { Spacing = 8, Children = { extensionBox } };
        var catPanel = new StackPanel { Spacing = 8, Children = { categoryCombo } };
        var namePanel = new StackPanel { Spacing = 8, Children = { nameBox } };
        var scPanel = new StackPanel { Spacing = 8, Children = { shortcutBox } };
        var agePanel = new StackPanel { Spacing = 8, Children = { ageMin, ageMax } };
        var sizePanel = new StackPanel { Spacing = 8, Children = { sizeMin, sizeMax } };

        void UpdateVisibility()
        {
            var k = (SortingRuleKind)kindCombo.SelectedIndex;
            extPanel.Visibility = k == SortingRuleKind.Extension ? Visibility.Visible : Visibility.Collapsed;
            catPanel.Visibility = k == SortingRuleKind.FileCategory ? Visibility.Visible : Visibility.Collapsed;
            namePanel.Visibility = k == SortingRuleKind.NameContains ? Visibility.Visible : Visibility.Collapsed;
            scPanel.Visibility = k == SortingRuleKind.ShortcutTarget ? Visibility.Visible : Visibility.Collapsed;
            agePanel.Visibility = k == SortingRuleKind.Age ? Visibility.Visible : Visibility.Collapsed;
            sizePanel.Visibility = k == SortingRuleKind.Size ? Visibility.Visible : Visibility.Collapsed;
        }
        kindCombo.SelectionChanged += (_, _) => UpdateVisibility();
        UpdateVisibility();

        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        panel.Children.Add(kindCombo);
        panel.Children.Add(extPanel);
        panel.Children.Add(catPanel);
        panel.Children.Add(namePanel);
        panel.Children.Add(scPanel);
        panel.Children.Add(agePanel);
        panel.Children.Add(sizePanel);
        panel.Children.Add(targetCombo);
        panel.Children.Add(priorityBox);

        var dialog = new ContentDialog
        {
            Title = Loc.Get(isEdit ? "Rules_EditDialogTitle" : "Rules_AddDialogTitle"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 500 },
            PrimaryButtonText = Loc.Get(isEdit ? "Common_Save" : "Common_Add"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        if (targetCombo.SelectedIndex < 0 || targetCombo.SelectedIndex >= _spaces.Count)
        {
            ShowStatus(Loc.Get("Rules_TargetRequired"), InfoBarSeverity.Warning);
            return;
        }

        var chosenKind = (SortingRuleKind)kindCombo.SelectedIndex;
        rule.Kind = chosenKind;
        rule.Priority = (int)priorityBox.Value;

        var target = _spaces[targetCombo.SelectedIndex];
        rule.TargetSpaceId = target.Id;
        rule.TargetSpaceTitle = target.Title;

        switch (chosenKind)
        {
            case SortingRuleKind.Extension:
                string ext = extensionBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(ext))
                {
                    ShowStatus(Loc.Get("Rules_ExtensionRequired"), InfoBarSeverity.Warning);
                    return;
                }
                rule.Pattern = ext;
                rule.ExtensionPattern = ext;
                break;
            case SortingRuleKind.FileCategory:
                rule.Category = (FileCategory)categoryCombo.SelectedIndex;
                break;
            case SortingRuleKind.NameContains:
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    ShowStatus(Loc.Get("Rules_NameRequired"), InfoBarSeverity.Warning);
                    return;
                }
                rule.Pattern = nameBox.Text.Trim();
                break;
            case SortingRuleKind.ShortcutTarget:
                if (string.IsNullOrWhiteSpace(shortcutBox.Text))
                {
                    ShowStatus(Loc.Get("Rules_ShortcutRequired"), InfoBarSeverity.Warning);
                    return;
                }
                rule.Pattern = shortcutBox.Text.Trim();
                break;
            case SortingRuleKind.Age:
                rule.MinAgeDays = (int)ageMin.Value;
                rule.MaxAgeDays = ageMax.Value < 0 ? int.MaxValue : (int)ageMax.Value;
                break;
            case SortingRuleKind.Size:
                rule.MinSizeBytes = (long)(sizeMin.Value * 1024);
                rule.MaxSizeBytes = sizeMax.Value < 0 ? long.MaxValue : (long)(sizeMax.Value * 1024);
                break;
        }

        if (!isEdit)
            _rules.Add(rule);

        SortingRuleStore.Save(_rules);
        RebuildList();
        ShowStatus(
            Loc.Get(isEdit ? "Rules_Updated" : "Rules_Added"),
            InfoBarSeverity.Success);
    }

    private int NextPriority()
    {
        int max = 99;
        foreach (var r in _rules) if (r.Priority > max) max = r.Priority;
        return max + 1;
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
        {
            _rules.RemoveAll(r => r.Id == id);
            SortingRuleStore.Save(_rules);
            RebuildList();
            ShowStatus(Loc.Get("Rules_Removed"), InfoBarSeverity.Informational);
        }
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }
}
