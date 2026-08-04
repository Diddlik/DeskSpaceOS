using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DeskSpaceOS.Core.Models;
using DeskSpaceOS.Core.Storage;
using DeskSpaceOS.Core.Win32;

namespace DeskSpaceOS.Service.Controls;

public partial class SpaceControl : System.Windows.Controls.UserControl
{
    private readonly IntPtr _listViewHandle;
    private List<int> _capturedIconIndices = new List<int>();

    // Tab support
    private List<SpaceTab> _tabs = new();
    private int _activeTabIndex = 0;
    private Dictionary<Guid, List<int>> _tabIconIndices = new();
    private int _renamingTabIndex = -1;

    public IReadOnlyList<SpaceTab> Tabs => _tabs;
    public IReadOnlyDictionary<Guid, List<int>> TabIconIndices => _tabIconIndices;

    /// <summary>All icon indices managed by this space across every tab.</summary>
    public IEnumerable<int> AllManagedIconIndices => _tabIconIndices.Values.SelectMany(x => x).Distinct();

    private bool _isRolledUp;
    private double _expandedHeight;
    private bool _isApplyingModel;

    public const int HeaderHeight = 32;
    public const int ContentTop = HeaderHeight; // tabs are inside the header

    public Guid SpaceId { get; set; } = Guid.NewGuid();

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
        UpdateCapturedIconsPositions();
        RaiseStateChanged();
    }

    /// <summary>Raised whenever state changes that should be persisted.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised after this space is removed from its parent.</summary>
    public event EventHandler? Deleted;

    public SpaceControl()
    {
        InitializeComponent();
        _listViewHandle = DesktopManager.GetDesktopListViewHandle();
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

    public Space ToModel()
    {
        // Save active tab's icons before serializing
        SaveActiveTabIcons();

        var tabModels = new List<SpaceTab>();
        foreach (var tab in _tabs)
        {
            var tabModel = new SpaceTab { Id = tab.Id, Name = tab.Name };
            if (_tabIconIndices.TryGetValue(tab.Id, out var indices))
            {
                foreach (int idx in indices)
                {
                    string? name = _listViewHandle != IntPtr.Zero ? ListViewManager.GetItemText(_listViewHandle, idx) : null;
                    if (!string.IsNullOrEmpty(name))
                        tabModel.IconNames.Add(name);
                }
            }
            tabModels.Add(tabModel);
        }

        return new Space
        {
            Id = SpaceId,
            Title = Title,
            X = Canvas.GetLeft(this),
            Y = Canvas.GetTop(this),
            Width = this.Width,
            Height = this.Height,
            IsRolledUp = _isRolledUp,
            ExpandedHeight = _isRolledUp ? _expandedHeight : (this.ActualHeight > 0 ? this.ActualHeight : this.Height),
            ColorR = _currentColor.R,
            ColorG = _currentColor.G,
            ColorB = _currentColor.B,
            Alpha = _currentAlpha,
            IconNames = GetCapturedIconNames(), // backward compat: active tab's icons
            Tabs = tabModels,
            ActiveTabIndex = _activeTabIndex
        };
    }

    public void ApplyModel(Space model)
    {
        _isApplyingModel = true;
        try
        {
            SpaceId = model.Id;
            this.Width = model.Width;
            this.Height = model.Height;
            _currentColor = System.Windows.Media.Color.FromRgb(model.ColorR, model.ColorG, model.ColorB);
            _currentAlpha = model.Alpha;
            UpdateBackgroundColor();

            // Restore tabs
            _tabs.Clear();
            _tabIconIndices.Clear();

            if (model.Tabs != null && model.Tabs.Count > 0)
            {
                foreach (var t in model.Tabs)
                {
                    _tabs.Add(new SpaceTab { Id = t.Id, Name = t.Name });
                    _tabIconIndices[t.Id] = new List<int>(); // restored later by RestoreTabIconsByName
                }
                _activeTabIndex = Math.Clamp(model.ActiveTabIndex, 0, _tabs.Count - 1);
            }
            else
            {
                // Migration: create a single tab from legacy IconNames
                var defaultTab = new SpaceTab { Name = string.IsNullOrWhiteSpace(model.Title) ? "Tab 1" : model.Title };
                _tabs.Add(defaultTab);
                _tabIconIndices[defaultTab.Id] = new List<int>();
                _activeTabIndex = 0;
            }

            if (_tabs.Count > 0
                && _tabs.Count == 1
                && !string.IsNullOrWhiteSpace(model.Title)
                && string.Equals(_tabs[0].Name, "Tab 1", StringComparison.OrdinalIgnoreCase))
            {
                _tabs[0].Name = model.Title;
            }

            RebuildTabStrip();
        }
        finally
        {
            _isApplyingModel = false;
        }
    }

    public void ApplyAppearance(byte colorR, byte colorG, byte colorB, byte alpha)
    {
        _currentColor = System.Windows.Media.Color.FromRgb(colorR, colorG, colorB);
        _currentAlpha = alpha;
        UpdateBackgroundColor();
        RaiseStateChanged();
    }

    private List<string> GetCapturedIconNames()
    {
        var names = new List<string>();
        if (_listViewHandle == IntPtr.Zero) return names;
        foreach (int idx in _capturedIconIndices)
        {
            string? name = ListViewManager.GetItemText(_listViewHandle, idx);
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }
        return names;
    }

    public void RestoreCapturedIconsByName(List<string> iconNames)
    {
        if (_listViewHandle == IntPtr.Zero || iconNames.Count == 0) return;

        _capturedIconIndices.Clear();
        int count = ListViewManager.GetItemCount(_listViewHandle);
        for (int i = 0; i < count; i++)
        {
            string? name = ListViewManager.GetItemText(_listViewHandle, i);
            if (name != null && iconNames.Contains(name))
                _capturedIconIndices.Add(i);
        }

        // Sync to active tab
        if (_tabs.Count > 0)
            _tabIconIndices[_tabs[_activeTabIndex].Id] = new List<int>(_capturedIconIndices);
    }

    /// <summary>
    /// Restores icons for all tabs from the model, hiding inactive tabs' icons off-screen.
    /// </summary>
    public void RestoreAllTabIcons(Space model)
    {
        if (_listViewHandle == IntPtr.Zero) return;

        int count = ListViewManager.GetItemCount(_listViewHandle);
        // Build name-to-index lookup
        var nameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            string? name = ListViewManager.GetItemText(_listViewHandle, i);
            if (name != null && !nameToIndex.ContainsKey(name))
                nameToIndex[name] = i;
        }

        for (int t = 0; t < _tabs.Count && t < model.Tabs.Count; t++)
        {
            var tabModel = model.Tabs[t];
            var indices = new List<int>();
            foreach (string iconName in tabModel.IconNames)
            {
                if (nameToIndex.TryGetValue(iconName, out int idx))
                    indices.Add(idx);
            }
            _tabIconIndices[_tabs[t].Id] = indices;

            if (t != _activeTabIndex)
            {
                // Hide inactive tab's icons off-screen
                foreach (int idx in indices)
                    ListViewManager.SetItemPosition(_listViewHandle, idx, -10000, -10000);
            }
        }

        // Set active tab's icons as current
        _capturedIconIndices = new List<int>(_tabIconIndices[_tabs[_activeTabIndex].Id]);
    }

    private void RaiseStateChanged()
    {
        if (_isApplyingModel) return;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    
    private IntPtr GetWindowHandle()
    {
        var window = Window.GetWindow(this);
        if (window != null)
        {
            return new WindowInteropHelper(window).Handle;
        }
        return IntPtr.Zero;
    }
    
    private void EnableActivation()
    {
        IntPtr hwnd = GetWindowHandle();
        if (hwnd != IntPtr.Zero)
        {
            int exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
            // Remove both NOACTIVATE and TRANSPARENT to allow keyboard focus
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
            // Restore both NOACTIVATE and TRANSPARENT
            exStyle |= User32.WS_EX_NOACTIVATE;
            exStyle |= User32.WS_EX_TRANSPARENT;
            User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE, exStyle);
        }
    }

    public string Title
    {
        get => _tabs.Count > 0 ? _tabs[_activeTabIndex].Name : "New Space";
        set
        {
            if (_tabs.Count > 0)
            {
                _tabs[_activeTabIndex].Name = value;
                RebuildTabStrip();
            }
        }
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Double-click handling is done via mouse hook in OverlayWindow
        // Dragging is also handled via mouse hook
        e.Handled = true;
    }
    
    public void CloseContextMenu()
    {
        var headerBorder = this.FindName("HeaderBorder") as System.Windows.Controls.Border;
        if (headerBorder?.ContextMenu != null && headerBorder.ContextMenu.IsOpen)
            headerBorder.ContextMenu.IsOpen = false;
    }

    public bool IsContextMenuOpen
    {
        get
        {
            var headerBorder = this.FindName("HeaderBorder") as System.Windows.Controls.Border;
            return headerBorder?.ContextMenu?.IsOpen == true;
        }
    }

    public void ShowContextMenu(double screenX, double screenY)
    {
        // Get the context menu from the header border
        var headerBorder = this.FindName("HeaderBorder") as System.Windows.Controls.Border;
        if (headerBorder?.ContextMenu != null)
        {
            headerBorder.ContextMenu.IsOpen = true;
        }
        else
        {
            // Fallback - create and show context menu
            var menu = new System.Windows.Controls.ContextMenu();
            
            var renameItem = new System.Windows.Controls.MenuItem { Header = "Rename" };
            renameItem.Click += MenuItem_Rename_Click;
            menu.Items.Add(renameItem);

            var rollUpItem = new System.Windows.Controls.MenuItem { Header = "Roll Up / Expand" };
            rollUpItem.Click += MenuItem_RollUp_Click;
            menu.Items.Add(rollUpItem);
            
            menu.Items.Add(new System.Windows.Controls.Separator());
            
            var bgMenu = new System.Windows.Controls.MenuItem { Header = "Background Color" };
            var darkItem = new System.Windows.Controls.MenuItem { Header = "Dark" };
            darkItem.Click += MenuItem_BgDark_Click;
            bgMenu.Items.Add(darkItem);
            var lightItem = new System.Windows.Controls.MenuItem { Header = "Light" };
            lightItem.Click += MenuItem_BgLight_Click;
            bgMenu.Items.Add(lightItem);
            var blueItem = new System.Windows.Controls.MenuItem { Header = "Blue" };
            blueItem.Click += MenuItem_BgBlue_Click;
            bgMenu.Items.Add(blueItem);
            var greenItem = new System.Windows.Controls.MenuItem { Header = "Green" };
            greenItem.Click += MenuItem_BgGreen_Click;
            bgMenu.Items.Add(greenItem);
            var purpleItem = new System.Windows.Controls.MenuItem { Header = "Purple" };
            purpleItem.Click += MenuItem_BgPurple_Click;
            bgMenu.Items.Add(purpleItem);
            menu.Items.Add(bgMenu);
            
            var transMenu = new System.Windows.Controls.MenuItem { Header = "Transparency" };
            var trans25 = new System.Windows.Controls.MenuItem { Header = "25%" };
            trans25.Click += MenuItem_Trans25_Click;
            transMenu.Items.Add(trans25);
            var trans50 = new System.Windows.Controls.MenuItem { Header = "50%" };
            trans50.Click += MenuItem_Trans50_Click;
            transMenu.Items.Add(trans50);
            var trans75 = new System.Windows.Controls.MenuItem { Header = "75%" };
            trans75.Click += MenuItem_Trans75_Click;
            transMenu.Items.Add(trans75);
            menu.Items.Add(transMenu);
            
            menu.Items.Add(new System.Windows.Controls.Separator());

            var sortMenu = new System.Windows.Controls.MenuItem { Header = "Sort Icons" };
            var sortByName = new System.Windows.Controls.MenuItem { Header = "By Name" };
            sortByName.Click += MenuItem_SortByName_Click;
            sortMenu.Items.Add(sortByName);
            var autoArrange = new System.Windows.Controls.MenuItem { Header = "Auto-Arrange" };
            autoArrange.Click += MenuItem_AutoArrange_Click;
            sortMenu.Items.Add(autoArrange);
            menu.Items.Add(sortMenu);

            menu.Items.Add(new System.Windows.Controls.Separator());

            var deleteItem = new System.Windows.Controls.MenuItem { Header = "Delete Space" };
            deleteItem.Click += MenuItem_Delete_Click;
            menu.Items.Add(deleteItem);
            
            menu.PlacementTarget = this;
            menu.IsOpen = true;
        }
    }
    
    /// <summary>
    /// Finds icons within bounds and lays them out in a grid.
    /// Use for initial capture (new space creation).
    /// </summary>
    public void CaptureIconsWithinBounds()
    {
        ScanIconsWithinBounds();
        if (_capturedIconIndices.Count > 0)
            UpdateCapturedIconsPositions();
    }

    /// <summary>
    /// Re-scans which icons are within bounds without repositioning them.
    /// Use after move/resize to update the captured set.
    /// </summary>
    public void RescanCapturedIcons()
    {
        ScanIconsWithinBounds();
    }

    private void ScanIconsWithinBounds()
    {
        if (_listViewHandle == IntPtr.Zero) return;

        this.UpdateLayout();

        double spaceLeft = Canvas.GetLeft(this);
        double spaceTop = Canvas.GetTop(this);
        double w = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
        double h = this.ActualHeight > 0 ? this.ActualHeight : this.Height;
        double spaceRight = spaceLeft + w;
        double spaceBottom = spaceTop + h;

        _capturedIconIndices.Clear();

        int iconCount = ListViewManager.GetItemCount(_listViewHandle);
        for (int i = 0; i < iconCount; i++)
        {
            var pos = ListViewManager.GetItemPosition(_listViewHandle, i);
            if (pos.HasValue)
            {
                double iconCenterX = pos.Value.X + 40;
                double iconCenterY = pos.Value.Y + 40;

                if (iconCenterX >= spaceLeft && iconCenterX <= spaceRight &&
                    iconCenterY >= spaceTop && iconCenterY <= spaceBottom)
                {
                    _capturedIconIndices.Add(i);
                }
            }
        }
    }

    private Dictionary<int, POINT>? _dragStartIconPositions;

    /// <summary>Snapshot current icon positions before a drag begins.</summary>
    public void BeginMoveIcons()
    {
        if (_listViewHandle == IntPtr.Zero || _capturedIconIndices.Count == 0) return;

        _dragStartIconPositions = new Dictionary<int, POINT>();
        foreach (int idx in _capturedIconIndices)
        {
            var pos = ListViewManager.GetItemPosition(_listViewHandle, idx);
            if (pos.HasValue)
                _dragStartIconPositions[idx] = pos.Value;
        }
    }

    /// <summary>Move icons by total offset from their drag-start positions.</summary>
    public void ApplyMoveOffset(double totalDeltaX, double totalDeltaY)
    {
        if (_listViewHandle == IntPtr.Zero || _dragStartIconPositions == null) return;

        foreach (var kvp in _dragStartIconPositions)
        {
            ListViewManager.SetItemPosition(_listViewHandle, kvp.Key,
                kvp.Value.X + (int)totalDeltaX, kvp.Value.Y + (int)totalDeltaY);
        }
    }

    public void EndMoveIcons()
    {
        _dragStartIconPositions = null;
    }
    
    private double _scrollOffset = 0;
    private double _scrollableHeight = 0;

    public void ScrollBy(int delta)
    {
        if (_scrollableHeight <= 0) return;
        
        _scrollOffset -= delta;
        if (_scrollOffset < 0) _scrollOffset = 0;
        if (_scrollOffset > _scrollableHeight) _scrollOffset = _scrollableHeight;
        
        UpdateCapturedIconsPositions();
    }

    public bool IsPointOnScrollbar(double localX, double localY)
    {
        if (ScrollThumb.Visibility != Visibility.Visible) return false;
        double w = ActualWidth > 0 ? ActualWidth : Width;
        return localX >= w - 16 && localY > ContentTop;
    }

    public void ScrollToFraction(double localY)
    {
        if (_scrollableHeight <= 0) return;
        
        double h = (ActualHeight > 0 ? ActualHeight : Height) - ContentTop;
        if (h <= 0) return;
        
        double fraction = Math.Clamp((localY - ContentTop) / h, 0, 1);
        _scrollOffset = fraction * _scrollableHeight;
        
        UpdateCapturedIconsPositions();
    }

    private void UpdateScrollbarThumb()
    {
        if (_scrollableHeight > 0)
        {
            ScrollThumb.Visibility = Visibility.Visible;
            double visibleHeight = (ActualHeight > 0 ? ActualHeight : Height) - ContentTop;
            double totalHeight = visibleHeight + _scrollableHeight;
            double thumbHeight = Math.Max(20, visibleHeight * (visibleHeight / totalHeight));
            ScrollThumb.Height = thumbHeight;
            
            double maxThumbY = visibleHeight - thumbHeight;
            double fraction = _scrollableHeight > 0 ? _scrollOffset / _scrollableHeight : 0;
            double thumbY = ContentTop + fraction * maxThumbY;
            
            ScrollThumb.Margin = new Thickness(0, thumbY, 2, 0);
        }
        else
        {
            ScrollThumb.Visibility = Visibility.Collapsed;
        }
    }

    public void UpdateCapturedIconsPositions()
    {
        if (_listViewHandle == IntPtr.Zero || _capturedIconIndices.Count == 0)
        {
            _scrollableHeight = 0;
            UpdateScrollbarThumb();
            return;
        }

        double spaceLeft = Canvas.GetLeft(this);
        double spaceTop = Canvas.GetTop(this);
        double spaceHeight = ActualHeight > 0 ? ActualHeight : Height;
        double visibleHeight = spaceHeight - ContentTop;
        double spaceWidth = ActualWidth > 0 ? ActualWidth : Width;

        int padding = 10;
        int iconWidth = 80;
        int iconHeight = 100;

        int columns = Math.Max(1, (int)(spaceWidth - padding) / iconWidth);
        int rows = (int)Math.Ceiling((double)_capturedIconIndices.Count / columns);
        double totalContentHeight = rows * iconHeight + padding;

        _scrollableHeight = Math.Max(0, totalContentHeight - visibleHeight);
        if (_scrollOffset > _scrollableHeight) _scrollOffset = _scrollableHeight;
        
        UpdateScrollbarThumb();

        int col = 0;
        int row = 0;

        foreach (int iconIndex in _capturedIconIndices)
        {
            int x = (int)spaceLeft + padding + col * iconWidth;
            int y = (int)spaceTop + ContentTop + padding + row * iconHeight;
            
            int actualY = (int)(y - _scrollOffset);

            // Hide if its center falls outside the visible area of the space
            if (actualY < (int)(spaceTop + ContentTop - (iconHeight / 2)) || 
                actualY > (int)(spaceTop + spaceHeight - (iconHeight / 2)))
            {
                ListViewManager.SetItemPosition(_listViewHandle, iconIndex, -10000, -10000);
            }
            else
            {
                ListViewManager.SetItemPosition(_listViewHandle, iconIndex, x, actualY);
            }

            col++;
            if (col >= columns)
            {
                col = 0;
                row++;
            }
        }
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
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

    private void MenuItem_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (this.Parent is System.Windows.Controls.Panel parentPanel)
        {
            parentPanel.Children.Remove(this);
            Deleted?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StartRename()
    {
        // Rename the active tab
        StartTabRename(_activeTabIndex);
    }
    
    // --- Tab management ---

    public void SaveActiveTabIcons()
    {
        if (_tabs.Count > 0 && _activeTabIndex < _tabs.Count)
            _tabIconIndices[_tabs[_activeTabIndex].Id] = new List<int>(_capturedIconIndices);
    }

    public void SwitchToTab(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _activeTabIndex) return;

        // Save current tab's icons
        SaveActiveTabIcons();

        // Hide current tab's icons off-screen
        if (_listViewHandle != IntPtr.Zero)
        {
            foreach (int idx in _capturedIconIndices)
                ListViewManager.SetItemPosition(_listViewHandle, idx, -10000, -10000);
        }

        // Switch
        _activeTabIndex = index;

        // Load new tab's icons
        _capturedIconIndices = _tabIconIndices.TryGetValue(_tabs[index].Id, out var indices)
            ? new List<int>(indices)
            : new List<int>();

        // Show new tab's icons in the grid
        UpdateCapturedIconsPositions();
        RebuildTabStrip();
        RaiseStateChanged();
    }

    public void AddTab()
    {
        var tab = new SpaceTab { Name = $"Tab {_tabs.Count + 1}" };
        _tabs.Add(tab);
        _tabIconIndices[tab.Id] = new List<int>();
        SwitchToTab(_tabs.Count - 1);
    }

    public void CloseTab(int index)
    {
        if (_tabs.Count <= 1 || index < 0 || index >= _tabs.Count) return;

        // Save current first
        SaveActiveTabIcons();

        var tab = _tabs[index];

        // Release this tab's icons (move to free space, not off-screen)
        if (_tabIconIndices.TryGetValue(tab.Id, out var icons) && _listViewHandle != IntPtr.Zero)
        {
            // Just leave them wherever they are; they become "free" icons
        }
        _tabIconIndices.Remove(tab.Id);
        _tabs.RemoveAt(index);

        // Adjust active index
        if (_activeTabIndex >= _tabs.Count)
            _activeTabIndex = _tabs.Count - 1;

        // Load the now-active tab's icons
        _capturedIconIndices = _tabIconIndices.TryGetValue(_tabs[_activeTabIndex].Id, out var newIcons)
            ? new List<int>(newIcons)
            : new List<int>();

        UpdateCapturedIconsPositions();
        RebuildTabStrip();
        RaiseStateChanged();
    }

    private System.Windows.Controls.TextBox? _activeRenameBox;
    private bool _isCommittingRename;

    public void StartTabRename(int tabIndex)
    {
        // If already renaming, commit the current one first
        if (_renamingTabIndex >= 0) CommitTabRename();

        if (tabIndex < 0 || tabIndex >= _tabs.Count) return;
        if (tabIndex >= TabStrip.Children.Count) return;
        _renamingTabIndex = tabIndex;

        EnableActivation();
        var window = Window.GetWindow(this);
        window?.Activate();

        // Replace the specific tab Border with a TextBox at the same position
        double editWidth = Math.Max(100, _tabs[tabIndex].Name.Length * 9 + 40);

        var editBox = new System.Windows.Controls.TextBox
        {
            Text = _tabs[tabIndex].Name,
            Foreground = new SolidColorBrush(System.Windows.Media.Colors.White),
            CaretBrush = new SolidColorBrush(System.Windows.Media.Colors.White),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI, sans-serif"),
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
        // Fire several focus attempts with increasing delays.
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
            RaiseStateChanged();
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

    public void HideAllIcons()
    {
        if (_listViewHandle == IntPtr.Zero) return;

        foreach (var indices in _tabIconIndices.Values)
        {
            foreach (int idx in indices)
            {
                ListViewManager.SetItemPosition(_listViewHandle, idx, -10000, -10000);
            }
        }
        foreach (int idx in _capturedIconIndices)
        {
            ListViewManager.SetItemPosition(_listViewHandle, idx, -10000, -10000);
        }
    }

    public void MergeSpace(SpaceControl other)
    {
        other.SaveActiveTabIcons();
        SaveActiveTabIcons();

        foreach (var tab in other.Tabs)
        {
            var newTab = new SpaceTab { Id = tab.Id, Name = tab.Name };
            _tabs.Add(newTab);
            
            if (other.TabIconIndices.TryGetValue(tab.Id, out var indices))
            {
                _tabIconIndices[newTab.Id] = new List<int>(indices);
            }
            else
            {
                _tabIconIndices[newTab.Id] = new List<int>();
            }
        }

        other.HideAllIcons();
        RebuildTabStrip();
        RaiseStateChanged();
    }


    /// <summary>
    /// Returns the tab index for a local X coordinate in the tab strip area, or -2 for "+" button, or -1 for none.
    /// </summary>
    public int GetTabIndexAtLocalX(double localX)
    {
        double x = 4; // left margin of tab strip
        for (int i = 0; i < _tabs.Count; i++)
        {
            double tabWidth = 60; // approximate per-tab width
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
        // Check "+" button
        if (localX >= x && localX < x + 24)
            return -2; // special: add tab
        return -1;
    }

    /// <summary>
    /// Checks if a local X is on the close button of a given tab.
    /// </summary>
    public bool IsCloseButtonHit(double localX, int tabIndex)
    {
        if (_tabs.Count <= 1) return false; // can't close last tab
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
        // Close button is the rightmost 16px of the tab
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
                Foreground = new SolidColorBrush(isActive
                    ? System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
                    : System.Windows.Media.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI, sans-serif"),
                FontSize = 14,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 120
            };

            var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(nameBlock);

            // Close button (only if more than 1 tab)
            if (_tabs.Count > 1)
            {
                var closeBtn = new TextBlock
                {
                    Text = "\u00D7",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                sp.Children.Add(closeBtn);
            }

            // Active tab gets a subtle bottom highlight via background
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

        // "+" button (only show if we have tabs already)
        if (_tabs.Count > 0)
        {
            var addBorder = new Border
            {
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(6, 0, 6, 0),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = "+",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                    FontSize = 14,
                    FontWeight = FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            TabStrip.Children.Add(addBorder);
        }
    }

    private static System.Windows.Media.Brush GetTabBackground(TabStyle tabStyle, bool isActive)
    {
        if (!isActive) return System.Windows.Media.Brushes.Transparent;

        return tabStyle switch
        {
            TabStyle.Menu => System.Windows.Media.Brushes.Transparent,
            TabStyle.Flat => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
            TabStyle.Segmented => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))
        };
    }

    private static System.Windows.Media.Brush GetTabBorderBrush(TabStyle tabStyle, bool isActive)
    {
        return tabStyle == TabStyle.Segmented && isActive
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))
            : System.Windows.Media.Brushes.Transparent;
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

    private void ResizeGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        double newWidth = this.Width + e.HorizontalChange;
        double newHeight = this.Height + e.VerticalChange;
        
        if (newWidth >= 100) this.Width = newWidth;
        if (newHeight >= 80) this.Height = newHeight;
        
        UpdateCapturedIconsPositions();
    }
    
    private byte _currentAlpha = 0x40;
    private System.Windows.Media.Color _currentColor = System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A);
    
    private void UpdateBackgroundColor()
    {
        BackgroundBorder.Background = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(_currentAlpha, _currentColor.R, _currentColor.G, _currentColor.B));
        RaiseStateChanged();
    }
    
    private void MenuItem_BgDark_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A);
        UpdateBackgroundColor();
    }
    
    private void MenuItem_BgLight_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0);
        UpdateBackgroundColor();
    }
    
    private void MenuItem_BgBlue_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = System.Windows.Media.Color.FromRgb(0x00, 0x50, 0x80);
        UpdateBackgroundColor();
    }
    
    private void MenuItem_BgGreen_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = System.Windows.Media.Color.FromRgb(0x20, 0x60, 0x20);
        UpdateBackgroundColor();
    }
    
    private void MenuItem_BgPurple_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = System.Windows.Media.Color.FromRgb(0x50, 0x20, 0x60);
        UpdateBackgroundColor();
    }
    
    private void MenuItem_Trans25_Click(object sender, RoutedEventArgs e)
    {
        _currentAlpha = 0x40; // 25%
        UpdateBackgroundColor();
    }
    
    private void MenuItem_Trans50_Click(object sender, RoutedEventArgs e)
    {
        _currentAlpha = 0x80; // 50%
        UpdateBackgroundColor();
    }
    
    private void MenuItem_Trans75_Click(object sender, RoutedEventArgs e)
    {
        _currentAlpha = 0xC0; // 75%
        UpdateBackgroundColor();
    }

    // --- Icon sorting ---

    private void MenuItem_SortByName_Click(object sender, RoutedEventArgs e)
    {
        SortCapturedIcons();
    }

    private void MenuItem_AutoArrange_Click(object sender, RoutedEventArgs e)
    {
        UpdateCapturedIconsPositions();
    }

    private void SortCapturedIcons()
    {
        if (_listViewHandle == IntPtr.Zero || _capturedIconIndices.Count < 2) return;

        // Build (index, name) pairs
        var items = new List<(int Index, string Name)>();
        foreach (int idx in _capturedIconIndices)
        {
            string? name = ListViewManager.GetItemText(_listViewHandle, idx);
            items.Add((idx, name ?? ""));
        }

        // Sort by name
        items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // Update the indices list in sorted order
        _capturedIconIndices.Clear();
        foreach (var item in items)
            _capturedIconIndices.Add(item.Index);

        // Reposition in the grid
        UpdateCapturedIconsPositions();
        RaiseStateChanged();
    }

    // --- Drag-and-drop icon support ---

    /// <summary>Show the drop-target highlight.</summary>
    public void ShowDropHighlight()
    {
        DropHighlightBorder.Visibility = Visibility.Visible;
    }

    /// <summary>Hide the drop-target highlight.</summary>
    public void HideDropHighlight()
    {
        DropHighlightBorder.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Accept an icon that was dragged and dropped onto this space.
    /// Adds it to the captured set and places it in the next free grid slot
    /// without moving existing icons.
    /// </summary>
    /// <returns>True if the icon was accepted (not already captured).</returns>
    public bool AcceptDroppedIcon(int iconIndex)
    {
        if (_listViewHandle == IntPtr.Zero) return false;
        if (_capturedIconIndices.Contains(iconIndex)) return true;

        _capturedIconIndices.Add(iconIndex);
        SaveActiveTabIcons(); // sync to _tabIconIndices immediately
        UpdateCapturedIconsPositions();
        RaiseStateChanged();
        return true;
    }

    /// <summary>
    /// Remove an icon from this space's captured set (e.g. when dragged out).
    /// </summary>
    public bool RemoveIcon(int iconIndex)
    {
        bool removed = _capturedIconIndices.Remove(iconIndex);
        if (removed)
        {
            SaveActiveTabIcons(); // sync to _tabIconIndices immediately
            RaiseStateChanged();
        }
        return removed;
    }

    /// <summary>Returns true if this space currently owns the given icon index.</summary>
    public bool ContainsIcon(int iconIndex) => _capturedIconIndices.Contains(iconIndex);

    /// <summary>
    /// Re-hides all inactive tabs' icons off-screen.
    /// Call after any operation that may cause Explorer to reposition icons (e.g. drag-drop).
    /// </summary>
    public void RehideInactiveTabIcons()
    {
        if (_listViewHandle == IntPtr.Zero) return;

        for (int t = 0; t < _tabs.Count; t++)
        {
            if (t == _activeTabIndex) continue;
            if (_tabIconIndices.TryGetValue(_tabs[t].Id, out var indices))
            {
                foreach (int idx in indices)
                    ListViewManager.SetItemPosition(_listViewHandle, idx, -10000, -10000);
            }
        }
    }
}
