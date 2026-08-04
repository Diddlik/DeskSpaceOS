using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WpfMedia = System.Windows.Media;
using DeskSpaceOS.Core.Models;
using DeskSpaceOS.Core.Storage;
using DeskSpaceOS.Core.Win32;

namespace DeskSpaceOS.Service.Controls;

public partial class PortalSpaceControl : System.Windows.Controls.UserControl
{
    public Guid PortalId { get; set; } = Guid.NewGuid();
    public string DirectoryPath { get; set; } = string.Empty;

    // Tab support
    private List<PortalTab> _tabs = new();
    private int _activeTabIndex = 0;
    private Dictionary<Guid, List<FileEntry>> _tabFileEntries = new();
    private int _renamingTabIndex = -1;

    // Navigation (Phase 4)
    private bool _enableNavigation = false;
    public bool EnableNavigation => _enableNavigation;

    private bool _isRolledUp;
    private double _expandedHeight;

    public const int HeaderHeight = 32;
    public const int BreadcrumbHeight = 22;
    public const int ContentTop = HeaderHeight; // tabs are inside the header

    /// <summary>Total Y offset before file content begins (header + optional breadcrumb row).</summary>
    private int EffectiveContentTop => ContentTop + (BreadcrumbBar.Visibility == Visibility.Visible ? BreadcrumbHeight : 0);

    public bool IsRolledUp => _isRolledUp;

    public void ToggleRollUp()
    {
        if (_isRolledUp)
        {
            this.Height = _expandedHeight;
            _isRolledUp = false;
        }
        else
        {
            _expandedHeight = this.ActualHeight > 0 ? this.ActualHeight : this.Height;
            this.Height = ContentTop;
            _isRolledUp = true;
        }
        
        ApplyViewMode();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the portal is resized or moved so the overlay can persist.</summary>
    public event EventHandler? StateChanged;

    private readonly List<FileEntry> _fileEntries = new();

    // View/sort state
    private PortalViewMode _viewMode = PortalViewMode.Icons;
    private bool _showName = true;
    private bool _showDate = true;
    private bool _showSize = true;
    private PortalSortColumn _sortColumn = PortalSortColumn.Name;
    private bool _sortAscending = true;

    // Color/transparency state
    private byte _currentAlpha = 0x60;
    private WpfMedia.Color _currentColor = WpfMedia.Color.FromRgb(0x00, 0x30, 0x50);
    private string? _selectedFilePath;
    private System.Windows.Controls.ContextMenu? _fileContextMenu;

    public PortalSpaceControl()
    {
        InitializeComponent();
        UpdateHeaderVisibility(false);
        this.Loaded += (s, e) => UpdateHeaderVisibility(IsMouseOver);
    }

    private void UpdateHeaderVisibility(bool isMouseOver)
    {
        var settings = AppSettingsStore.Load();
        if (settings.HeaderVisibility == HeaderVisibility.Always)
        {
            HeaderBorder.Opacity = 1.0;
        }
        else if (settings.HeaderVisibility == HeaderVisibility.Never)
        {
            HeaderBorder.Opacity = 0.0;
        }
        else if (settings.HeaderVisibility == HeaderVisibility.OnMouseOver)
        {
            HeaderBorder.Opacity = isMouseOver ? 1.0 : 0.0;
        }
    }

    private void UserControl_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdateHeaderVisibility(true);
    }

    private void UserControl_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdateHeaderVisibility(false);
    }

    public string Title
    {
        get => _tabs.Count > 0 ? _tabs[_activeTabIndex].Name : "Portal";
        set
        {
            if (_tabs.Count > 0)
            {
                _tabs[_activeTabIndex].Name = value;
                RebuildTabStrip();
            }
        }
    }

    public void ApplyModel(FolderPortal model)
    {
        PortalId = model.Id;
        Title = model.Title;
        Width = model.Width;
        Height = model.Height;

        _currentColor = WpfMedia.Color.FromRgb(model.ColorR, model.ColorG, model.ColorB);
        _currentAlpha = model.Alpha;
        UpdateBackgroundColor();

        _viewMode = model.ViewMode;
        _showName = model.ShowNameColumn;
        _showDate = model.ShowDateColumn;
        _showSize = model.ShowSizeColumn;
        _sortColumn = model.SortColumn;
        _sortAscending = model.SortAscending;
        _enableNavigation = model.EnableNavigation;

        // Restore tabs
        _tabs.Clear();
        _tabFileEntries.Clear();

        if (model.Tabs != null && model.Tabs.Count > 0)
        {
            foreach (var t in model.Tabs)
            {
                _tabs.Add(new PortalTab
                {
                    Id = t.Id,
                    Name = t.Name,
                    DirectoryPath = t.DirectoryPath,
                    CurrentPath = string.IsNullOrEmpty(t.CurrentPath) ? t.DirectoryPath : t.CurrentPath
                });
                _tabFileEntries[t.Id] = new List<FileEntry>();
            }
            _activeTabIndex = Math.Clamp(model.ActiveTabIndex, 0, _tabs.Count - 1);
            DirectoryPath = _tabs[_activeTabIndex].CurrentPath;
        }
        else
        {
            // Migration: single-directory portal → one tab
            var defaultTab = new PortalTab { Name = "Tab 1", DirectoryPath = model.DirectoryPath, CurrentPath = model.DirectoryPath };
            _tabs.Add(defaultTab);
            _tabFileEntries[defaultTab.Id] = new List<FileEntry>();
            _activeTabIndex = 0;
            DirectoryPath = model.DirectoryPath;
        }

        RebuildTabStrip();
        ApplyViewMode();
        ApplyColumnVisibility();
        UpdateSortIndicators();
        UpdateMenuChecks();
        UpdateBreadcrumb();
    }

    public FolderPortal ToModel()
    {
        // Save active tab's current path + file entries (home stays untouched)
        if (_tabs.Count > 0 && _activeTabIndex < _tabs.Count)
        {
            _tabs[_activeTabIndex].CurrentPath = DirectoryPath;
            _tabFileEntries[_tabs[_activeTabIndex].Id] = new List<FileEntry>(_fileEntries);
        }

        var tabModels = _tabs.Select(t => new PortalTab
        {
            Id = t.Id,
            Name = t.Name,
            DirectoryPath = t.DirectoryPath,
            CurrentPath = t.CurrentPath
        }).ToList();

        return new FolderPortal
        {
            Id = PortalId,
            Title = Title,
            DirectoryPath = DirectoryPath, // backward compat
            X = Canvas.GetLeft(this),
            Y = Canvas.GetTop(this),
            Width = Width,
            Height = Height,
            IsRolledUp = _isRolledUp,
            ExpandedHeight = _isRolledUp ? _expandedHeight : (this.ActualHeight > 0 ? this.ActualHeight : this.Height),
            ColorR = _currentColor.R,
            ColorG = _currentColor.G,
            ColorB = _currentColor.B,
            Alpha = _currentAlpha,
            ViewMode = _viewMode,
            ShowNameColumn = _showName,
            ShowDateColumn = _showDate,
            ShowSizeColumn = _showSize,
            SortColumn = _sortColumn,
            SortAscending = _sortAscending,
            Tabs = tabModels,
            ActiveTabIndex = _activeTabIndex,
            EnableNavigation = _enableNavigation
        };
    }

    // --- File management ---

    public void RefreshFiles()
    {
        _fileEntries.Clear();
        FileItemsPanel.Children.Clear();
        DetailsItemsPanel.Children.Clear();

        if (string.IsNullOrEmpty(DirectoryPath) || !Directory.Exists(DirectoryPath))
        {
            EmptyLabel.Visibility = Visibility.Visible;
            CountBadge.Text = "not found";
            return;
        }

        try
        {
            var entries = Directory.GetFileSystemEntries(DirectoryPath);

            foreach (string fullPath in entries)
            {
                string name = Path.GetFileName(fullPath);
                bool isDir = Directory.Exists(fullPath);
                long size = 0;
                DateTime modified = DateTime.MinValue;

                try
                {
                    if (isDir)
                    {
                        modified = Directory.GetLastWriteTime(fullPath);
                    }
                    else
                    {
                        var fi = new FileInfo(fullPath);
                        size = fi.Length;
                        modified = fi.LastWriteTime;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Unreadable entry: list it with unknown size/date instead of dropping it.
                    size = 0;
                    modified = DateTime.MinValue;
                }

                _fileEntries.Add(new FileEntry
                {
                    FullPath = fullPath,
                    Name = name,
                    IsDirectory = isDir,
                    Size = size,
                    DateModified = modified
                });
            }

            SortAndRebuild();
        }
        catch
        {
            EmptyLabel.Text = "Access denied";
            EmptyLabel.Visibility = Visibility.Visible;
        }
    }

    public void AddFileEntry(string fullPath)
    {
        if (_fileEntries.Any(e => e.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
            return;

        string name = Path.GetFileName(fullPath);
        bool isDir = Directory.Exists(fullPath);
        long size = 0;
        DateTime modified = DateTime.MinValue;

        try
        {
            if (isDir)
            {
                modified = Directory.GetLastWriteTime(fullPath);
            }
            else
            {
                var fi = new FileInfo(fullPath);
                size = fi.Length;
                modified = fi.LastWriteTime;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable entry: list it with unknown size/date instead of dropping it.
            size = 0;
            modified = DateTime.MinValue;
        }

        _fileEntries.Add(new FileEntry
        {
            FullPath = fullPath,
            Name = name,
            IsDirectory = isDir,
            Size = size,
            DateModified = modified
        });

        SortAndRebuild();
    }

    public void RemoveFileEntry(string fullPath)
    {
        int idx = _fileEntries.FindIndex(e => e.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;

        _fileEntries.RemoveAt(idx);
        SortAndRebuild();
    }

    public void RenameFileEntry(string oldPath, string newPath)
    {
        RemoveFileEntry(oldPath);
        AddFileEntry(newPath);
    }

    // --- Sorting & rebuilding ---

    private void SortAndRebuild()
    {
        if (_selectedFilePath != null && !_fileEntries.Any(e => PathEquals(e.FullPath, _selectedFilePath)))
            _selectedFilePath = null;

        // Sort: directories first, then by selected column
        var sorted = _fileEntries
            .OrderBy(e => e.IsDirectory ? 0 : 1)
            .ThenBy(e => e, new FileEntryComparer(_sortColumn, _sortAscending))
            .ToList();

        _fileEntries.Clear();
        _fileEntries.AddRange(sorted);

        RebuildIconsView();
        RebuildDetailsView();
        UpdateSortIndicators();

        EmptyLabel.Visibility = _fileEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountBadge.Text = $"{_fileEntries.Count} item(s)";
    }

    private void RebuildIconsView()
    {
        FileItemsPanel.Children.Clear();
        foreach (var entry in _fileEntries)
            FileItemsPanel.Children.Add(BuildIconVisual(entry));
    }

    private void RebuildDetailsView()
    {
        DetailsItemsPanel.Children.Clear();
        foreach (var entry in _fileEntries)
            DetailsItemsPanel.Children.Add(BuildDetailsRow(entry));
    }

    // --- Icon view visual ---

    private FrameworkElement BuildIconVisual(FileEntry entry)
    {
        FrameworkElement iconElement;
        var shellIcon = ShellIconExtractor.GetIcon(entry.FullPath, entry.IsDirectory);
        if (shellIcon != null)
        {
            iconElement = new System.Windows.Controls.Image
            {
                Source = shellIcon,
                Width = 32,
                Height = 32,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
        }
        else
        {
            iconElement = new TextBlock
            {
                Text = GetGlyph(entry),
                FontFamily = new WpfMedia.FontFamily("Segoe MDL2 Assets"),
                FontSize = 26,
                Foreground = GetGlyphBrush(entry),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
        }

        string displayName = entry.Name;
        if (displayName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
            displayName.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            displayName = Path.GetFileNameWithoutExtension(displayName);
        }

        var label = new TextBlock
        {
            Text = displayName,
            Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 68,
            MaxHeight = 30,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        var stack = new StackPanel
        {
            Width = 72,
            Margin = new Thickness(2, 4, 2, 4)
        };

        stack.Children.Add(iconElement);
        stack.Children.Add(label);

        var border = new Border
        {
            Width = 76,
            MinHeight = 70,
            Padding = new Thickness(2),
            Margin = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = GetItemBackground(entry.FullPath, hover: false),
            BorderBrush = GetItemBorderBrush(entry.FullPath),
            BorderThickness = new Thickness(PathEquals(_selectedFilePath, entry.FullPath) ? 1 : 0),
            Tag = entry.FullPath,
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = stack
        };

        border.MouseEnter += (_, _) => ApplyItemState(border, entry.FullPath, hover: true);
        border.MouseLeave += (_, _) => ApplyItemState(border, entry.FullPath, hover: false);

        return border;
    }

    // --- Details view row ---

    private FrameworkElement BuildDetailsRow(FileEntry entry)
    {
        string glyph = GetGlyph(entry);

        var row = new Grid
        {
            Tag = entry.FullPath,
            Cursor = System.Windows.Input.Cursors.Hand,
            Height = 22,
            Background = GetItemBackground(entry.FullPath, hover: false) // enables hit-test on empty space
        };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        // Sync column widths with header visibility
        row.ColumnDefinitions[0].Width = ColDefName.Width;
        row.ColumnDefinitions[1].Width = _showDate ? new GridLength(110) : new GridLength(0);
        row.ColumnDefinitions[2].Width = _showSize ? new GridLength(70) : new GridLength(0);

        // Name column (always has icon + name)
        var namePanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

        FrameworkElement rowIcon;
        var shellIcon = ShellIconExtractor.GetIcon(entry.FullPath, entry.IsDirectory);
        if (shellIcon != null)
        {
            rowIcon = new System.Windows.Controls.Image
            {
                Source = shellIcon,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
        }
        else
        {
            rowIcon = new TextBlock
            {
                Text = glyph,
                FontFamily = new WpfMedia.FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = GetGlyphBrush(entry),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
        }
        namePanel.Children.Add(rowIcon);

        string displayName = entry.Name;
        if (displayName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
            displayName.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            displayName = Path.GetFileNameWithoutExtension(displayName);

        namePanel.Children.Add(new TextBlock
        {
            Text = displayName,
            Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        Grid.SetColumn(namePanel, 0);
        row.Children.Add(namePanel);

        // Date column
        if (_showDate)
        {
            var dateText = new TextBlock
            {
                Text = entry.DateModified > DateTime.MinValue ? entry.DateModified.ToString("yyyy-MM-dd HH:mm") : "",
                Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(dateText, 1);
            row.Children.Add(dateText);
        }

        // Size column
        if (_showSize)
        {
            var sizeText = new TextBlock
            {
                Text = entry.IsDirectory ? "" : FormatSize(entry.Size),
                Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(sizeText, 2);
            row.Children.Add(sizeText);
        }

        // Hover effect
        row.MouseEnter += (_, _) => row.Background = GetItemBackground(entry.FullPath, hover: true);
        row.MouseLeave += (_, _) => row.Background = GetItemBackground(entry.FullPath, hover: false);

        return row;
    }

    // --- File opening ---

    /// <summary>
    /// Try to open the file at the given canvas coordinate.
    /// Returns true if a file was found and opened.
    /// </summary>
    public bool TryOpenFileAt(double canvasX, double canvasY)
    {
        double localX = canvasX - Canvas.GetLeft(this);
        double localY = canvasY - Canvas.GetTop(this);

        int effectiveTop = EffectiveContentTop;

        // Must be below header + optional breadcrumb
        if (localY < effectiveTop) return false;

        if (_viewMode == PortalViewMode.Details)
        {
            // In details mode, below effective top + column headers (24px)
            if (localY < effectiveTop + 24) return false;
            return TryGetFilePathFromPanel(DetailsItemsPanel, localX, localY - (effectiveTop + 24), out string? filePath)
                   && filePath != null
                   && OpenOrNavigateFile(filePath);
        }
        else
        {
            return TryGetFilePathFromPanel(FileItemsPanel, localX - 4, localY - (effectiveTop + 2), out string? filePath)
                   && filePath != null
                   && OpenOrNavigateFile(filePath);
        }
    }

    public bool SelectFileAt(double canvasX, double canvasY)
    {
        if (TryGetFilePathAt(canvasX, canvasY, out string? filePath))
        {
            SelectFile(filePath);
            return true;
        }

        SelectFile(null);
        return false;
    }

    public bool ShowFileContextMenuAt(double canvasX, double canvasY)
    {
        if (!TryGetFilePathAt(canvasX, canvasY, out string? filePath))
        {
            SelectFile(null);
            return false;
        }

        SelectFile(filePath);
        if (filePath != null)
            ShowFileContextMenu(filePath);
        return true;
    }

    private bool TryGetFilePathAt(double canvasX, double canvasY, out string? filePath)
    {
        filePath = null;
        double localX = canvasX - Canvas.GetLeft(this);
        double localY = canvasY - Canvas.GetTop(this);

        int effectiveTop = EffectiveContentTop;
        if (localY < effectiveTop) return false;

        if (_viewMode == PortalViewMode.Details)
        {
            if (localY < effectiveTop + 24) return false;
            return TryGetFilePathFromPanel(DetailsItemsPanel, localX, localY - (effectiveTop + 24), out filePath);
        }

        return TryGetFilePathFromPanel(FileItemsPanel, localX - 4, localY - (effectiveTop + 2), out filePath);
    }

    private bool TryGetFilePathFromPanel(System.Windows.Controls.Panel panel, double localX, double localY, out string? filePath)
    {
        filePath = null;
        var point = new System.Windows.Point(localX, localY);
        var hit = panel.InputHitTest(point) as DependencyObject;

        while (hit != null && hit != panel)
        {
            if (hit is FrameworkElement fe && fe.Tag is string hitFilePath)
            {
                filePath = hitFilePath;
                return true;
            }
            hit = WpfMedia.VisualTreeHelper.GetParent(hit);
        }

        return false;
    }

    private bool OpenOrNavigateFile(string filePath)
    {
        if (_enableNavigation && Directory.Exists(filePath))
        {
            NavigateTo(filePath);
            return true;
        }

        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UiLog.Warn(ex, "Failed to open {Path} via the shell.", filePath);
        }
        return true;
    }

    // --- Navigation (Phase 4) ---

    public void NavigateTo(string path)
    {
        if (!Directory.Exists(path)) return;
        DirectoryPath = path;
        if (_tabs.Count > 0)
            _tabs[_activeTabIndex].CurrentPath = path;
        RefreshFiles();
        UpdateBreadcrumb();
        StateChanged?.Invoke(this, EventArgs.Empty);
        TabSwitched?.Invoke(this, EventArgs.Empty); // reuses watcher-rewatch event
    }

    public void NavigateUp()
    {
        if (string.IsNullOrEmpty(DirectoryPath)) return;
        try
        {
            var parent = Directory.GetParent(DirectoryPath);
            if (parent != null) NavigateTo(parent.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            UiLog.Warn(ex, "Failed to resolve the parent of {Path} — staying in place.", DirectoryPath);
        }
    }

    public void NavigateHome()
    {
        if (_tabs.Count == 0) return;
        var home = _tabs[_activeTabIndex].DirectoryPath;
        if (!string.IsNullOrEmpty(home)) NavigateTo(home);
    }

    private void UpdateBreadcrumb()
    {
        bool atHome = _tabs.Count == 0
                      || string.Equals(DirectoryPath?.TrimEnd('\\'),
                                       _tabs[_activeTabIndex].DirectoryPath?.TrimEnd('\\'),
                                       StringComparison.OrdinalIgnoreCase);

        bool show = _enableNavigation && !atHome;
        BreadcrumbBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) BreadcrumbPath.Text = DirectoryPath ?? string.Empty;
    }

    private void Breadcrumb_Home_Click(object sender, MouseButtonEventArgs e) => NavigateHome();
    private void Breadcrumb_Up_Click(object sender, MouseButtonEventArgs e) => NavigateUp();

    private void MenuItem_EnableNavigation_Click(object sender, RoutedEventArgs e)
    {
        _enableNavigation = MenuEnableNavigation.IsChecked;
        if (!_enableNavigation) NavigateHome(); // reset view when turning off
        UpdateBreadcrumb();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MenuItem_GoHome_Click(object sender, RoutedEventArgs e) => NavigateHome();
    private void MenuItem_GoUp_Click(object sender, RoutedEventArgs e) => NavigateUp();

    // --- View mode ---

    private void ApplyViewMode()
    {
        if (_viewMode == PortalViewMode.Details)
        {
            IconsScrollViewer.Visibility = Visibility.Collapsed;
            DetailsView.Visibility = Visibility.Visible;
        }
        else
        {
            IconsScrollViewer.Visibility = Visibility.Visible;
            DetailsView.Visibility = Visibility.Collapsed;
        }
    }

    private void SetViewMode(PortalViewMode mode)
    {
        _viewMode = mode;
        ApplyViewMode();
        UpdateMenuChecks();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // --- Column visibility ---

    private void ApplyColumnVisibility()
    {
        // Name column is always visible (it's the star column)
        ColDefDate.Width = _showDate ? new GridLength(110) : new GridLength(0);
        ColDefSize.Width = _showSize ? new GridLength(70) : new GridLength(0);

        HeaderDate.Visibility = _showDate ? Visibility.Visible : Visibility.Collapsed;
        HeaderDateSort.Visibility = _showDate ? Visibility.Visible : Visibility.Collapsed;
        HeaderSize.Visibility = _showSize ? Visibility.Visible : Visibility.Collapsed;
        HeaderSizeSort.Visibility = _showSize ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Sort indicators ---

    private void UpdateSortIndicators()
    {
        string arrow = _sortAscending ? "\u25B2" : "\u25BC";
        HeaderNameSort.Text = _sortColumn == PortalSortColumn.Name ? arrow : "";
        HeaderDateSort.Text = _sortColumn == PortalSortColumn.DateModified ? arrow : "";
        HeaderSizeSort.Text = _sortColumn == PortalSortColumn.Size ? arrow : "";
    }

    private void UpdateMenuChecks()
    {
        MenuViewIcons.IsChecked = _viewMode == PortalViewMode.Icons;
        MenuViewDetails.IsChecked = _viewMode == PortalViewMode.Details;
        MenuColName.IsChecked = _showName;
        MenuColDate.IsChecked = _showDate;
        MenuColSize.IsChecked = _showSize;
        MenuSortName.IsChecked = _sortColumn == PortalSortColumn.Name;
        MenuSortDate.IsChecked = _sortColumn == PortalSortColumn.DateModified;
        MenuSortSize.IsChecked = _sortColumn == PortalSortColumn.Size;
        MenuSortAsc.IsChecked = _sortAscending;
        MenuSortDesc.IsChecked = !_sortAscending;
        MenuEnableNavigation.IsChecked = _enableNavigation;
    }

    // --- Menu event handlers ---

    public void ShowContextMenu()
    {
        if (HeaderBorder.ContextMenu != null)
            HeaderBorder.ContextMenu.IsOpen = true;
    }

    public void CloseContextMenu()
    {
        if (HeaderBorder.ContextMenu != null && HeaderBorder.ContextMenu.IsOpen)
            HeaderBorder.ContextMenu.IsOpen = false;
        if (_fileContextMenu != null && _fileContextMenu.IsOpen)
            _fileContextMenu.IsOpen = false;
    }

    public bool IsContextMenuOpen => HeaderBorder.ContextMenu?.IsOpen == true || _fileContextMenu?.IsOpen == true;

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        ShowContextMenu();
    }

    // --- Rename ---

    private IntPtr GetWindowHandle()
    {
        var window = Window.GetWindow(this);
        if (window != null)
            return new WindowInteropHelper(window).Handle;
        return IntPtr.Zero;
    }

    private void EnableActivation()
    {
        IntPtr hwnd = GetWindowHandle();
        if (hwnd != IntPtr.Zero)
        {
            int exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
            exStyle &= ~User32.WS_EX_NOACTIVATE;
            exStyle &= ~User32.WS_EX_TRANSPARENT;
            User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE, exStyle);
            User32.SetForegroundWindow(hwnd);
        }
    }

    private void DisableActivation()
    {
        IntPtr hwnd = GetWindowHandle();
        if (hwnd != IntPtr.Zero)
        {
            int exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
            exStyle |= User32.WS_EX_NOACTIVATE;
            exStyle |= User32.WS_EX_TRANSPARENT;
            User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE, exStyle);
        }
    }

    public void StartRename()
    {
        // Rename the active tab
        StartTabRename(_activeTabIndex);
    }

    private void MenuItem_Rename_Click(object sender, RoutedEventArgs e)
    {
        StartRename();
    }

    private void MenuItem_AddTab_Click(object sender, RoutedEventArgs e)
    {
        AddTab();
    }

    private void MenuItem_RollUp_Click(object sender, RoutedEventArgs e)
    {
        if (!AppSettingsStore.Load().EnableRollUp) return;
        ToggleRollUp();
    }

    private void MenuItem_ViewIcons_Click(object sender, RoutedEventArgs e) => SetViewMode(PortalViewMode.Icons);
    private void MenuItem_ViewDetails_Click(object sender, RoutedEventArgs e) => SetViewMode(PortalViewMode.Details);

    private void MenuItem_ToggleColumn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem mi) return;
        switch (mi.Tag as string)
        {
            case "Name": _showName = mi.IsChecked; break;
            case "Date": _showDate = mi.IsChecked; break;
            case "Size": _showSize = mi.IsChecked; break;
        }
        ApplyColumnVisibility();
        RebuildDetailsView();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MenuItem_Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem mi) return;
        _sortColumn = (mi.Tag as string) switch
        {
            "Date" => PortalSortColumn.DateModified,
            "Size" => PortalSortColumn.Size,
            _ => PortalSortColumn.Name
        };
        SortAndRebuild();
        UpdateMenuChecks();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MenuItem_SortDirection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem mi) return;
        _sortAscending = (mi.Tag as string) == "Asc";
        SortAndRebuild();
        UpdateMenuChecks();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ColumnHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string tag) return;

        var col = tag switch
        {
            "Date" => PortalSortColumn.DateModified,
            "Size" => PortalSortColumn.Size,
            _ => PortalSortColumn.Name
        };

        if (_sortColumn == col)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = col;
            _sortAscending = true;
        }

        SortAndRebuild();
        UpdateMenuChecks();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MenuItem_OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(DirectoryPath) && Directory.Exists(DirectoryPath))
        {
            try { Process.Start(new ProcessStartInfo(DirectoryPath) { UseShellExecute = true }); }
            catch (Exception ex) { UiLog.Warn(ex, "Failed to open folder {Path} in the shell.", DirectoryPath); }
        }
    }

    private void SelectFile(string? filePath)
    {
        _selectedFilePath = filePath;
        UpdateSelectionVisuals();
    }

    private void UpdateSelectionVisuals()
    {
        foreach (FrameworkElement element in FileItemsPanel.Children.OfType<FrameworkElement>())
            ApplyElementSelection(element, hover: false);

        foreach (FrameworkElement element in DetailsItemsPanel.Children.OfType<FrameworkElement>())
            ApplyElementSelection(element, hover: false);
    }

    private void ApplyElementSelection(FrameworkElement element, bool hover)
    {
        if (element.Tag is not string filePath) return;

        if (element is Border border)
            ApplyItemState(border, filePath, hover);
        else if (element is Grid grid)
            grid.Background = GetItemBackground(filePath, hover);
    }

    private void ApplyItemState(Border border, string filePath, bool hover)
    {
        border.Background = GetItemBackground(filePath, hover);
        border.BorderBrush = GetItemBorderBrush(filePath);
        border.BorderThickness = new Thickness(PathEquals(_selectedFilePath, filePath) ? 1 : 0);
    }

    private WpfMedia.Brush GetItemBackground(string filePath, bool hover)
    {
        if (PathEquals(_selectedFilePath, filePath))
            return new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x50, 0x4D, 0xA3, 0xFF));

        if (hover)
            return new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

        return WpfMedia.Brushes.Transparent;
    }

    private WpfMedia.Brush GetItemBorderBrush(string filePath)
    {
        return PathEquals(_selectedFilePath, filePath)
            ? new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0xA0, 0xA9, 0xD1, 0xFF))
            : WpfMedia.Brushes.Transparent;
    }

    private void ShowFileContextMenu(string filePath)
    {
        _fileContextMenu?.Items.Clear();

        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };

        if (Directory.Exists(filePath))
        {
            var navigateItem = new System.Windows.Controls.MenuItem { Header = "Open in Portal" };
            navigateItem.Click += (_, _) => NavigateTo(filePath);
            menu.Items.Add(navigateItem);
        }

        var openItem = new System.Windows.Controls.MenuItem { Header = "Open" };
        openItem.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true }); }
            catch (Exception ex) { UiLog.Warn(ex, "Failed to open {Path} via the shell.", filePath); }
        };
        menu.Items.Add(openItem);

        var copyItem = new System.Windows.Controls.MenuItem { Header = "Copy" };
        copyItem.Click += (_, _) => CopyFileToClipboard(filePath);
        menu.Items.Add(copyItem);

        var showItem = new System.Windows.Controls.MenuItem { Header = "Show in File Explorer" };
        showItem.Click += (_, _) => ShowInFileExplorer(filePath);
        menu.Items.Add(showItem);

        var copyPathItem = new System.Windows.Controls.MenuItem { Header = "Copy Path" };
        copyPathItem.Click += (_, _) =>
        {
            try { System.Windows.Clipboard.SetText(filePath); }
            catch (Exception ex) { UiLog.Warn(ex, "Failed to copy the path of {Path} to the clipboard.", filePath); }
        };
        menu.Items.Add(copyPathItem);

        var deleteItem = new System.Windows.Controls.MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => DeleteFileToRecycleBin(filePath);
        menu.Items.Add(deleteItem);

        var propertiesItem = new System.Windows.Controls.MenuItem { Header = "Properties" };
        propertiesItem.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(filePath) { Verb = "properties", UseShellExecute = true }); }
            catch (Exception ex) { UiLog.Warn(ex, "Failed to show shell properties for {Path}.", filePath); }
        };
        menu.Items.Add(new Separator());
        menu.Items.Add(propertiesItem);

        _fileContextMenu = menu;
        _fileContextMenu.IsOpen = true;
    }

    private static void CopyFileToClipboard(string filePath)
    {
        try
        {
            var files = new System.Collections.Specialized.StringCollection { filePath };
            System.Windows.Clipboard.SetFileDropList(files);
        }
        catch (Exception ex)
        {
            UiLog.Warn(ex, "Failed to copy {Path} to the clipboard.", filePath);
        }
    }

    private void DeleteFileToRecycleBin(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    filePath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else if (Directory.Exists(filePath))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    filePath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            SelectFile(null);
            RefreshFiles();
        }
        catch (Exception ex)
        {
            UiLog.Warn(ex, "Failed to move {Path} to the recycle bin.", filePath);
        }
    }

    private static void ShowInFileExplorer(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
            else if (Directory.Exists(filePath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UiLog.Warn(ex, "Failed to reveal {Path} in File Explorer.", filePath);
        }
    }

    private static bool PathEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private void MenuItem_Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshFiles();
    }

    // --- Scrolling ---

    public void ScrollBy(int delta)
    {
        var sv = ActiveScrollViewer;
        if (sv != null)
            sv.ScrollToVerticalOffset(sv.VerticalOffset - delta);
    }

    /// <summary>
    /// Check if a local point (relative to this control) is on the scrollbar track (rightmost 10px of the content area).
    /// </summary>
    public bool IsPointOnScrollbar(double localX, double localY)
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        return localX >= w - 10 && localY > EffectiveContentTop;
    }

    /// <summary>
    /// Scroll proportionally: maps a local Y position to a scroll offset.
    /// </summary>
    public void ScrollToFraction(double localY)
    {
        var sv = ActiveScrollViewer;
        if (sv == null || sv.ScrollableHeight <= 0) return;

        int effectiveTop = EffectiveContentTop;
        double h = (ActualHeight > 0 ? ActualHeight : Height) - effectiveTop;
        if (h <= 0) return;

        double fraction = Math.Clamp((localY - effectiveTop) / h, 0, 1);
        sv.ScrollToVerticalOffset(fraction * sv.ScrollableHeight);
    }

    private ScrollViewer? ActiveScrollViewer =>
        _viewMode == PortalViewMode.Details ? DetailsScrollViewer : IconsScrollViewer;

    // --- Tab management ---

    /// <summary>Raised when the active tab's directory path changes (for watcher re-registration).</summary>
    public event EventHandler? TabSwitched;

    public void SwitchToTab(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _activeTabIndex) return;

        // Save current tab
        _tabs[_activeTabIndex].CurrentPath = DirectoryPath;
        _tabFileEntries[_tabs[_activeTabIndex].Id] = new List<FileEntry>(_fileEntries);

        // Switch
        _activeTabIndex = index;
        DirectoryPath = !string.IsNullOrEmpty(_tabs[index].CurrentPath)
                        ? _tabs[index].CurrentPath
                        : _tabs[index].DirectoryPath;

        // Load new tab's cached files or refresh
        if (_tabFileEntries.TryGetValue(_tabs[index].Id, out var cached) && cached.Count > 0)
        {
            _fileEntries.Clear();
            _fileEntries.AddRange(cached);
            SortAndRebuild();
        }
        else
        {
            RefreshFiles();
        }

        RebuildTabStrip();
        UpdateBreadcrumb();
        StateChanged?.Invoke(this, EventArgs.Empty);
        TabSwitched?.Invoke(this, EventArgs.Empty);
    }

    public void AddTab()
    {
        // Save current tab state
        _tabs[_activeTabIndex].CurrentPath = DirectoryPath;
        _tabFileEntries[_tabs[_activeTabIndex].Id] = new List<FileEntry>(_fileEntries);

        var tab = new PortalTab { Name = $"Tab {_tabs.Count + 1}", DirectoryPath = DirectoryPath, CurrentPath = DirectoryPath };
        _tabs.Add(tab);
        _tabFileEntries[tab.Id] = new List<FileEntry>();

        _activeTabIndex = _tabs.Count - 1;
        DirectoryPath = tab.CurrentPath;
        RefreshFiles();
        RebuildTabStrip();
        UpdateBreadcrumb();
        StateChanged?.Invoke(this, EventArgs.Empty);
        TabSwitched?.Invoke(this, EventArgs.Empty);
    }

    public void CloseTab(int index)
    {
        if (_tabs.Count <= 1 || index < 0 || index >= _tabs.Count) return;

        _tabFileEntries.Remove(_tabs[index].Id);
        _tabs.RemoveAt(index);

        if (_activeTabIndex >= _tabs.Count)
            _activeTabIndex = _tabs.Count - 1;

        DirectoryPath = !string.IsNullOrEmpty(_tabs[_activeTabIndex].CurrentPath)
                        ? _tabs[_activeTabIndex].CurrentPath
                        : _tabs[_activeTabIndex].DirectoryPath;

        if (_tabFileEntries.TryGetValue(_tabs[_activeTabIndex].Id, out var cached) && cached.Count > 0)
        {
            _fileEntries.Clear();
            _fileEntries.AddRange(cached);
            SortAndRebuild();
        }
        else
        {
            RefreshFiles();
        }

        RebuildTabStrip();
        UpdateBreadcrumb();
        StateChanged?.Invoke(this, EventArgs.Empty);
        TabSwitched?.Invoke(this, EventArgs.Empty);
    }

    private System.Windows.Controls.TextBox? _activeRenameBox;
    private bool _isCommittingRename;

    public void StartTabRename(int tabIndex)
    {
        if (_renamingTabIndex >= 0) CommitTabRename();

        if (tabIndex < 0 || tabIndex >= _tabs.Count) return;
        if (tabIndex >= TabStrip.Children.Count) return;
        _renamingTabIndex = tabIndex;

        EnableActivation();
        var window = Window.GetWindow(this);
        window?.Activate();

        double editWidth = Math.Max(100, _tabs[tabIndex].Name.Length * 9 + 40);

        var editBox = new System.Windows.Controls.TextBox
        {
            Text = _tabs[tabIndex].Name,
            Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Colors.White),
            CaretBrush = new WpfMedia.SolidColorBrush(WpfMedia.Colors.White),
            Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            FontFamily = new WpfMedia.FontFamily("Segoe UI Variable, Segoe UI, sans-serif"),
            FontSize = 13,
            FontWeight = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 0, 6, 0),
            Width = editWidth,
            Height = 24,
            IsHitTestVisible = true
        };

        editBox.LostFocus += (_, _) => CommitTabRename();
        editBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) CommitTabRename();
            else if (e.Key == Key.Escape) CancelTabRename();
        };

        // Swap the tab Border for the TextBox at the same index
        TabStrip.Children.RemoveAt(tabIndex);
        TabStrip.Children.Insert(tabIndex, editBox);
        _activeRenameBox = editBox;

        // Focus needs multiple attempts because the overlay window
        // must first become activatable (WS_EX_TRANSPARENT removed).
        int[] focusDelays = { 50, 150, 300 };
        foreach (int delay in focusDelays)
        {
            var focusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(delay)
            };
            focusTimer.Tick += (_, _) =>
            {
                focusTimer.Stop();
                if (_activeRenameBox == null || _renamingTabIndex < 0) return;
                var wnd = Window.GetWindow(this);
                if (wnd != null)
                {
                    wnd.Activate();
                    var hwnd = new WindowInteropHelper(wnd).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        User32.SetForegroundWindow(hwnd);
                        User32.SetFocus(hwnd);
                    }
                }
                editBox.Focus();
                Keyboard.Focus(editBox);
                editBox.SelectAll();
            };
            focusTimer.Start();
        }
    }

    private void CommitTabRename()
    {
        if (_isCommittingRename) return;
        _isCommittingRename = true;
        try
        {
            if (_renamingTabIndex >= 0 && _renamingTabIndex < _tabs.Count && _activeRenameBox != null)
            {
                if (!string.IsNullOrWhiteSpace(_activeRenameBox.Text))
                    _tabs[_renamingTabIndex].Name = _activeRenameBox.Text;
            }
            _renamingTabIndex = -1;
            _activeRenameBox = null;
            DisableActivation();
            RebuildTabStrip();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _isCommittingRename = false; }
    }

    private void CancelTabRename()
    {
        if (_isCommittingRename) return;
        _isCommittingRename = true;
        try
        {
            _renamingTabIndex = -1;
            _activeRenameBox = null;
            DisableActivation();
            RebuildTabStrip();
        }
        finally { _isCommittingRename = false; }
    }


    public int GetTabIndexAtLocalX(double localX)
    {
        double x = 4;
        for (int i = 0; i < _tabs.Count; i++)
        {
            double tabWidth = 60;
            if (i < TabStrip.Children.Count)
            {
                var child = TabStrip.Children[i] as FrameworkElement;
                if (child != null && child.ActualWidth > 0)
                    tabWidth = child.ActualWidth;
            }
            if (localX >= x && localX < x + tabWidth)
                return i;
            x += tabWidth;
        }
        if (localX >= x && localX < x + 24)
            return -2; // add tab
        return -1;
    }

    public bool IsCloseButtonHit(double localX, int tabIndex)
    {
        if (_tabs.Count <= 1) return false;
        double x = 4;
        for (int i = 0; i < tabIndex && i < TabStrip.Children.Count; i++)
        {
            var child = TabStrip.Children[i] as FrameworkElement;
            x += (child != null && child.ActualWidth > 0) ? child.ActualWidth : 60;
        }
        double tabWidth = 60;
        if (tabIndex < TabStrip.Children.Count)
        {
            var tc = TabStrip.Children[tabIndex] as FrameworkElement;
            if (tc != null && tc.ActualWidth > 0) tabWidth = tc.ActualWidth;
        }
        return localX >= x + tabWidth - 16 && localX < x + tabWidth;
    }

    public int TabCount => _tabs.Count;
    public int ActiveTabIndex => _activeTabIndex;

    public void RefreshTabStyle()
    {
        RebuildTabStrip();
    }

    private void RebuildTabStrip()
    {
        TabStrip.Children.Clear();
        var tabStyle = AppSettingsStore.Load().TabStyle;

        for (int i = 0; i < _tabs.Count; i++)
        {
            bool isActive = i == _activeTabIndex;
            var tab = _tabs[i];

            var nameBlock = new TextBlock
            {
                Text = tab.Name,
                Foreground = new WpfMedia.SolidColorBrush(isActive
                    ? WpfMedia.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
                    : WpfMedia.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                FontFamily = new WpfMedia.FontFamily("Segoe UI Variable, Segoe UI, sans-serif"),
                FontSize = 14,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 120
            };

            var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(nameBlock);

            if (_tabs.Count > 1)
            {
                var closeBtn = new TextBlock
                {
                    Text = "\u00D7",
                    Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                sp.Children.Add(closeBtn);
            }

            var border = new Border
            {
                Background = GetTabBackground(tabStyle, isActive),
                BorderBrush = GetTabBorderBrush(tabStyle, isActive),
                BorderThickness = GetTabBorderThickness(tabStyle, isActive),
                CornerRadius = GetTabCornerRadius(tabStyle),
                Padding = GetTabPadding(tabStyle),
                Margin = GetTabMargin(tabStyle),
                Child = sp,
                IsHitTestVisible = false
            };

            TabStrip.Children.Add(border);
        }

        if (_tabs.Count > 0)
        {
            var addBorder = new Border
            {
                Background = WpfMedia.Brushes.Transparent,
                Padding = new Thickness(6, 0, 6, 0),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = "+",
                    Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                    FontSize = 14,
                    FontWeight = FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            TabStrip.Children.Add(addBorder);
        }
    }

    private static WpfMedia.Brush GetTabBackground(TabStyle tabStyle, bool isActive)
    {
        if (!isActive) return WpfMedia.Brushes.Transparent;

        return tabStyle switch
        {
            TabStyle.Menu => WpfMedia.Brushes.Transparent,
            TabStyle.Flat => new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
            TabStyle.Segmented => new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
            _ => new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))
        };
    }

    private static WpfMedia.Brush GetTabBorderBrush(TabStyle tabStyle, bool isActive)
    {
        return tabStyle == TabStyle.Segmented && isActive
            ? new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))
            : WpfMedia.Brushes.Transparent;
    }

    private static Thickness GetTabBorderThickness(TabStyle tabStyle, bool isActive)
    {
        return tabStyle == TabStyle.Segmented && isActive
            ? new Thickness(1)
            : new Thickness(0);
    }

    private static CornerRadius GetTabCornerRadius(TabStyle tabStyle)
    {
        return tabStyle switch
        {
            TabStyle.Menu => new CornerRadius(0),
            TabStyle.Flat => new CornerRadius(0),
            TabStyle.Segmented => new CornerRadius(5),
            _ => new CornerRadius(4, 4, 0, 0)
        };
    }

    private static Thickness GetTabPadding(TabStyle tabStyle)
    {
        return tabStyle == TabStyle.Menu
            ? new Thickness(8, 0, 6, 0)
            : new Thickness(10, 0, 8, 0);
    }

    private static Thickness GetTabMargin(TabStyle tabStyle)
    {
        return tabStyle == TabStyle.Segmented
            ? new Thickness(0, 3, 3, 3)
            : new Thickness(0);
    }

    // --- Color & transparency ---

    private void UpdateBackgroundColor()
    {
        BackgroundBorder.Background = new WpfMedia.SolidColorBrush(
            WpfMedia.Color.FromArgb(_currentAlpha, _currentColor.R, _currentColor.G, _currentColor.B));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MenuItem_BgDark_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = WpfMedia.Color.FromRgb(0x1A, 0x1A, 0x1A);
        UpdateBackgroundColor();
    }

    private void MenuItem_BgLight_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = WpfMedia.Color.FromRgb(0xE0, 0xE0, 0xE0);
        UpdateBackgroundColor();
    }

    private void MenuItem_BgBlue_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = WpfMedia.Color.FromRgb(0x00, 0x50, 0x80);
        UpdateBackgroundColor();
    }

    private void MenuItem_BgGreen_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = WpfMedia.Color.FromRgb(0x20, 0x60, 0x20);
        UpdateBackgroundColor();
    }

    private void MenuItem_BgPurple_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = WpfMedia.Color.FromRgb(0x50, 0x20, 0x60);
        UpdateBackgroundColor();
    }

    private void MenuItem_Trans25_Click(object sender, RoutedEventArgs e)
    {
        _currentAlpha = 0x40;
        UpdateBackgroundColor();
    }

    private void MenuItem_Trans50_Click(object sender, RoutedEventArgs e)
    {
        _currentAlpha = 0x80;
        UpdateBackgroundColor();
    }

    private void MenuItem_Trans75_Click(object sender, RoutedEventArgs e)
    {
        _currentAlpha = 0xC0;
        UpdateBackgroundColor();
    }

    // --- Helpers ---

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    private static string GetGlyph(FileEntry entry)
    {
        if (entry.IsDirectory) return "\uE8B7"; // Folder

        string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
        return ext switch
        {
            ".exe" or ".lnk" or ".msi" => "\uE737",
            ".txt" or ".log" or ".md" or ".csv" => "\uE8A5",
            ".doc" or ".docx" or ".rtf" => "\uE8A5",
            ".xls" or ".xlsx" => "\uE80A",
            ".pdf" => "\uEA90",
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".svg" or ".ico" or ".webp" => "\uEB9F",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".wma" => "\uE8D6",
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".webm" => "\uE714",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "\uF012",
            ".url" => "\uE774",
            ".ps1" or ".bat" or ".cmd" or ".sh" => "\uE756",
            ".cs" or ".cpp" or ".h" or ".py" or ".js" or ".ts" or ".json" or ".xml" or ".yaml" => "\uE943",
            _ => "\uE7C3"
        };
    }

    private static WpfMedia.SolidColorBrush GetGlyphBrush(FileEntry entry)
    {
        if (entry.IsDirectory)
            return new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0xFF, 0xD7, 0x54)); // gold/yellow

        string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
        var color = ext switch
        {
            ".exe" or ".lnk" or ".msi" => WpfMedia.Color.FromRgb(0x6D, 0xCB, 0xF5), // light blue
            ".txt" or ".log" or ".md" or ".csv" => WpfMedia.Color.FromRgb(0xCC, 0xCC, 0xCC), // light gray
            ".doc" or ".docx" or ".rtf" => WpfMedia.Color.FromRgb(0x4A, 0x86, 0xD9), // Word blue
            ".xls" or ".xlsx" => WpfMedia.Color.FromRgb(0x33, 0xA8, 0x54), // Excel green
            ".pdf" => WpfMedia.Color.FromRgb(0xE8, 0x4D, 0x3D), // PDF red
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".svg" or ".ico" or ".webp"
                => WpfMedia.Color.FromRgb(0x9B, 0x6D, 0xE3), // purple
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".wma"
                => WpfMedia.Color.FromRgb(0xF0, 0x80, 0x50), // orange
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".webm"
                => WpfMedia.Color.FromRgb(0xE0, 0x60, 0x80), // pinkish red
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz"
                => WpfMedia.Color.FromRgb(0xA0, 0x8C, 0x5A), // brownish
            ".url" => WpfMedia.Color.FromRgb(0x50, 0xB0, 0xF0), // link blue
            ".ps1" or ".bat" or ".cmd" or ".sh"
                => WpfMedia.Color.FromRgb(0x60, 0xD0, 0x60), // green
            ".cs" or ".cpp" or ".h" or ".py" or ".js" or ".ts" or ".json" or ".xml" or ".yaml"
                => WpfMedia.Color.FromRgb(0x56, 0xB6, 0xC2), // teal
            _ => WpfMedia.Color.FromRgb(0xBB, 0xBB, 0xBB) // default gray
        };
        return new WpfMedia.SolidColorBrush(color);
    }

    // --- Inner types ---

    private class FileEntry
    {
        public string FullPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime DateModified { get; set; }
    }

    private class FileEntryComparer : IComparer<FileEntry>
    {
        private readonly PortalSortColumn _column;
        private readonly bool _ascending;

        public FileEntryComparer(PortalSortColumn column, bool ascending)
        {
            _column = column;
            _ascending = ascending;
        }

        public int Compare(FileEntry? x, FileEntry? y)
        {
            if (x == null || y == null) return 0;

            int result = _column switch
            {
                PortalSortColumn.DateModified => x.DateModified.CompareTo(y.DateModified),
                PortalSortColumn.Size => x.Size.CompareTo(y.Size),
                _ => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)
            };

            return _ascending ? result : -result;
        }
    }
}
