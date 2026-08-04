using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using DeskSpaceOS.Core.Models;
using DeskSpaceOS.Core.Storage;
using DeskSpaceOS.Core.Win32;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;

namespace DeskSpaceOS.Service;

public partial class OverlayWindow : Window
{
    private IntPtr _workerWHandle;
    private bool _isDragging = false;
    private POINT _startPoint;
    private SelectionWindow? _selectionWindow;
    private bool _isDraggingElement = false; // true during move/resize of space or portal
    
    // Space interaction state
    private Controls.SpaceControl? _activeSpace = null;
    private bool _isMovingSpace = false;
    private bool _isResizingSpace = false;
    private bool _isScrollingSpace = false;
    private bool _isDraggingIcon = false;
    private List<int> _draggedIconIndices = new(); // ListView indices of the icons being dragged
    private Controls.SpaceControl? _dragSourceSpace = null; // space the icon was dragged FROM (null if free)
    private Controls.SpaceControl? _dropTargetSpace = null; // space currently highlighted for drop
    private POINT _dragOrigin; // mouse position at drag start (for total delta)
    private double _dragStartSpaceLeft; // canvas X of active space at drag start
    private double _dragStartSpaceTop;  // canvas Y of active space at drag start
    private double _unsnappedDragLeft; // canvas X before magnetic snapping is applied
    private double _unsnappedDragTop;  // canvas Y before magnetic snapping is applied
    
    // Folder portals
    private FolderPortalWatcher _portalWatcher = new();
    private Controls.PortalSpaceControl? _activePortal = null;
    private bool _isMovingPortal = false;
    private bool _isResizingPortal = false;
    private bool _isScrollingPortal = false;

    // Sorting rules (Phase 5)
    private DesktopFileMonitor _desktopMonitor = new();
    private List<SortingRule> _sortingRules = new();

    // ZenMode mode (Phase 6)
    private bool _zenModeFaded = false;
    private System.Windows.Threading.DispatcherTimer? _zenModeIdleTimer;
    private IntPtr _desktopListViewHandle = IntPtr.Zero;
    private IntPtr _shellDllDefViewHandle = IntPtr.Zero;

    // Settings hot-reload
    private readonly SettingsWatcher _settingsWatcher;
    private readonly ILogger? _logger;
    private AppSettings _currentSettings;
    private DateTime _lastSaveTime = DateTime.MinValue; // guard to ignore self-triggered file changes
    private const int SaveGuardMs = 500; // ignore file watcher events within this window after a save

    // Space creation popup state
    private Controls.CreateSpacePopup? _createSpacePopup = null;
    private double _pendingSpaceX, _pendingSpaceY, _pendingSpaceW, _pendingSpaceH;

    // DPI scaling: mouse hook reports physical pixels, WPF uses DIPs
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    public OverlayWindow(IntPtr workerWHandle, SettingsWatcher settingsWatcher, ILogger? logger = null)
    {
        InitializeComponent();
        _workerWHandle = workerWHandle;
        _settingsWatcher = settingsWatcher;
        _logger = logger;
        _currentSettings = AppSettingsStore.Load();

        // Size to cover the entire virtual screen
        this.Left = SystemParameters.VirtualScreenLeft;
        this.Top = SystemParameters.VirtualScreenTop;
        this.Width = SystemParameters.VirtualScreenWidth;
        this.Height = SystemParameters.VirtualScreenHeight;

        this.Loaded += OverlayWindow_Loaded;
        this.Unloaded += OverlayWindow_Unloaded;
    }

    /// <summary>
    /// Converts a mouse hook POINT (physical screen pixels) to WPF canvas coordinates (DIPs).
    /// </summary>
    private System.Windows.Point ScreenToCanvas(POINT pt)
    {
        return new System.Windows.Point(pt.X / _dpiScaleX - this.Left, pt.Y / _dpiScaleY - this.Top);
    }

    /// <summary>
    /// Converts a mouse hook POINT (physical screen pixels) to WPF DIPs (absolute screen).
    /// </summary>
    private System.Windows.Point ScreenToDip(POINT pt)
    {
        return new System.Windows.Point(pt.X / _dpiScaleX, pt.Y / _dpiScaleY);
    }

    private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Capture DPI scaling factor
        var dpiSource = PresentationSource.FromVisual(this);
        if (dpiSource?.CompositionTarget != null)
        {
            _dpiScaleX = dpiSource.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = dpiSource.CompositionTarget.TransformToDevice.M22;
        }

        WindowInteropHelper helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle;
        _overlayHwnd = hwnd;

        _desktopListViewHandle = DesktopManager.GetDesktopListViewHandle();
        if (_desktopListViewHandle != IntPtr.Zero)
        {
            _shellDllDefViewHandle = User32.GetParent(_desktopListViewHandle);

            // Ensure transparent background — these are Explorer defaults on modern Windows
            // but can be reset after the WorkerW injection.
            User32.SendMessage(_desktopListViewHandle, User32.LVM_SETBKCOLOR, IntPtr.Zero, (IntPtr)User32.CLR_NONE);
            User32.SendMessage(_desktopListViewHandle, User32.LVM_SETTEXTBKCOLOR, IntPtr.Zero, (IntPtr)User32.CLR_NONE);
            User32.SendMessage(_desktopListViewHandle, User32.LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)User32.LVS_EX_TRANSPARENTBKGND, (IntPtr)User32.LVS_EX_TRANSPARENTBKGND);
            // NOTE: do NOT add WS_EX_LAYERED — LWA_ALPHA=255 makes every pixel opaque,
            // which overrides LVS_EX_TRANSPARENTBKGND and hides the wallpaper.
        }

        // Make this window click-through (WS_EX_TRANSPARENT) so clicks pass to desktop icons
        // The spaces will handle their own hit-testing via mouse hook
        int windowExStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE, windowExStyle | User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE | User32.WS_EX_TRANSPARENT);

        // Place this window just above WorkerW in the z-order so it sits
        // on the desktop layer — visible on desktop but behind applications.
        PinToDesktopLayer(hwnd);

        // Hook WndProc to maintain z-order when Windows tries to reorder us
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        // Create the selection window (also pinned to desktop layer)
        _selectionWindow = new SelectionWindow(_workerWHandle);
        _selectionWindow.Show();
        
        // Start Global Mouse Hook
        MouseHook.OnLeftMouseDown += MouseHook_OnLeftMouseDown;
        MouseHook.OnLeftMouseUp += MouseHook_OnLeftMouseUp;
        MouseHook.OnRightMouseDown += MouseHook_OnRightMouseDown;
        MouseHook.OnMouseMove += MouseHook_OnMouseMove;
        MouseHook.OnLeftMouseDoubleClick += MouseHook_OnLeftMouseDoubleClick;
        MouseHook.OnMouseWheel += MouseHook_OnMouseWheel;
        MouseHook.Start();

        LoadSpaces();
        LoadPortals();

        // Subscribe to settings file changes for hot-reload
        _settingsWatcher.SpacesChanged += OnSpacesFileChanged;
        _settingsWatcher.PortalsChanged += OnPortalsFileChanged;
        _settingsWatcher.SettingsChanged += OnSettingsFileChanged;
        _settingsWatcher.SortingRulesChanged += OnSortingRulesFileChanged;

        // Load rules and start desktop monitor (Phase 5)
        _sortingRules = SortingRuleStore.Load();
        _desktopMonitor.FileArrived += OnDesktopFileArrived;
        _desktopMonitor.Start();

        RegisterHotkeys(hwnd);

        // Apply Quick Hide Start setting
        var appSettings = AppSettingsStore.Load();
        if (appSettings.EnableQuickHide && appSettings.QuickHideShowOnStart)
        {
            _isQuickHidden = false;
            OverlayCanvas.Visibility = Visibility.Visible;
            if (_desktopListViewHandle != IntPtr.Zero)
            {
                User32.ShowWindow(_desktopListViewHandle, User32.SW_SHOW);
            }
        }
    }

    private void OverlayWindow_Unloaded(object sender, RoutedEventArgs e)
    {
        WindowInteropHelper helper = new WindowInteropHelper(this);
        UnregisterHotkeys(helper.Handle);

        _settingsWatcher.SpacesChanged -= OnSpacesFileChanged;
        _settingsWatcher.PortalsChanged -= OnPortalsFileChanged;
        _settingsWatcher.SettingsChanged -= OnSettingsFileChanged;
        _settingsWatcher.SortingRulesChanged -= OnSortingRulesFileChanged;

        _desktopMonitor.FileArrived -= OnDesktopFileArrived;
        _desktopMonitor.Dispose();

        _portalWatcher.Dispose();

        MouseHook.Stop();
        MouseHook.OnLeftMouseDown -= MouseHook_OnLeftMouseDown;
        MouseHook.OnLeftMouseUp -= MouseHook_OnLeftMouseUp;
        MouseHook.OnRightMouseDown -= MouseHook_OnRightMouseDown;
        MouseHook.OnMouseMove -= MouseHook_OnMouseMove;
        MouseHook.OnLeftMouseDoubleClick -= MouseHook_OnLeftMouseDoubleClick;
        MouseHook.OnMouseWheel -= MouseHook_OnMouseWheel;

        // Restore icons on exit
        if (_desktopListViewHandle != IntPtr.Zero)
        {
            if (_hiddenFreeIconPositions.Count > 0)
                RestoreFreeIcons();
            User32.ShowWindow(_desktopListViewHandle, User32.SW_SHOW);
            User32.InvalidateRect(_desktopListViewHandle, IntPtr.Zero, true);
        }

        _selectionWindow?.Close();
    }
    
    private Controls.SpaceControl? GetSpaceAtPoint(POINT pt)
    {
        var pointOnCanvas = ScreenToCanvas(pt);
        
        foreach (var child in OverlayCanvas.Children)
        {
            if (child is Controls.SpaceControl space)
            {
                double left = Canvas.GetLeft(space);
                double top = Canvas.GetTop(space);
                double width = space.ActualWidth > 0 ? space.ActualWidth : space.Width;
                double height = space.ActualHeight > 0 ? space.ActualHeight : space.Height;
                
                if (pointOnCanvas.X >= left && pointOnCanvas.X <= left + width &&
                    pointOnCanvas.Y >= top && pointOnCanvas.Y <= top + height)
                {
                    return space;
                }
            }
        }
        return null;
    }
    
    private bool IsPointOnSpaceHeader(POINT pt, Controls.SpaceControl space)
    {
        var pointOnCanvas = ScreenToCanvas(pt);
        double left = Canvas.GetLeft(space);
        double top = Canvas.GetTop(space);
        double width = space.ActualWidth > 0 ? space.ActualWidth : space.Width;
        
        return pointOnCanvas.X >= left && pointOnCanvas.X <= left + width &&
               pointOnCanvas.Y >= top && pointOnCanvas.Y <= top + 32;
    }
    
    private bool IsPointOnMenuButton(POINT pt, Controls.SpaceControl space)
    {
        var pointOnCanvas = ScreenToCanvas(pt);
        double left = Canvas.GetLeft(space);
        double top = Canvas.GetTop(space);
        double width = space.ActualWidth > 0 ? space.ActualWidth : space.Width;
        double right = left + width;

        // Button is 32x32, right-aligned with 4px margin, within the 32px header
        return pointOnCanvas.X >= right - 36 && pointOnCanvas.X <= right &&
               pointOnCanvas.Y >= top && pointOnCanvas.Y <= top + 32;
    }

    private bool IsPointOnResizeGrip(POINT pt, Controls.SpaceControl space)
    {
        var pointOnCanvas = ScreenToCanvas(pt);
        double left = Canvas.GetLeft(space);
        double top = Canvas.GetTop(space);
        double width = space.ActualWidth > 0 ? space.ActualWidth : space.Width;
        double height = space.ActualHeight > 0 ? space.ActualHeight : space.Height;
        double right = left + width;
        double bottom = top + height;

        // Resize grip is 16x16 at bottom-right
        return pointOnCanvas.X >= right - 16 && pointOnCanvas.X <= right &&
               pointOnCanvas.Y >= bottom - 16 && pointOnCanvas.Y <= bottom;
    }

    // --- Portal hit-test helpers ---

    private bool IsPointOnPortalHeader(POINT pt, Controls.PortalSpaceControl portal)
    {
        var pointOnCanvas = ScreenToCanvas(pt);
        double left = Canvas.GetLeft(portal);
        double top = Canvas.GetTop(portal);
        double width = portal.ActualWidth > 0 ? portal.ActualWidth : portal.Width;

        return pointOnCanvas.X >= left && pointOnCanvas.X <= left + width &&
               pointOnCanvas.Y >= top && pointOnCanvas.Y <= top + 32;
    }

    private bool IsPointOnPortalMenuButton(POINT pt, Controls.PortalSpaceControl portal)
    {
        var pointOnCanvas = ScreenToCanvas(pt);
        double left = Canvas.GetLeft(portal);
        double top = Canvas.GetTop(portal);
        double width = portal.ActualWidth > 0 ? portal.ActualWidth : portal.Width;
        double right = left + width;

        return pointOnCanvas.X >= right - 36 && pointOnCanvas.X <= right &&
               pointOnCanvas.Y >= top && pointOnCanvas.Y <= top + 32;
    }

    private bool IsPointOnPortalResizeGrip(POINT pt, Controls.PortalSpaceControl portal)
    {
        var pointOnCanvas = ScreenToCanvas(pt);
        double left = Canvas.GetLeft(portal);
        double top = Canvas.GetTop(portal);
        double width = portal.ActualWidth > 0 ? portal.ActualWidth : portal.Width;
        double height = portal.ActualHeight > 0 ? portal.ActualHeight : portal.Height;
        double right = left + width;
        double bottom = top + height;

        return pointOnCanvas.X >= right - 16 && pointOnCanvas.X <= right &&
               pointOnCanvas.Y >= bottom - 16 && pointOnCanvas.Y <= bottom;
    }

    // --- Tab hit-testing (tabs are in the header) ---

    private double GetLocalX(POINT pt, FrameworkElement control)
    {
        var pointOnCanvas = ScreenToCanvas(pt);
        return pointOnCanvas.X - Canvas.GetLeft(control);
    }

    private bool IsDeskSpaceMouseOperationActive()
    {
        return _isDragging
            || _isDraggingElement
            || _isDraggingIcon
            || _isMovingSpace
            || _isResizingSpace
            || _isScrollingSpace
            || _isMovingPortal
            || _isResizingPortal
            || _isScrollingPortal;
    }

    private void RefreshDesktopLayerHandlesIfNeeded()
    {
        if (_desktopListViewHandle == IntPtr.Zero)
        {
            _desktopListViewHandle = DesktopManager.GetDesktopListViewHandle();
        }

        if (_desktopListViewHandle != IntPtr.Zero && _shellDllDefViewHandle == IntPtr.Zero)
        {
            _shellDllDefViewHandle = User32.GetParent(_desktopListViewHandle);
        }
    }

    private bool IsDesktopLayerMousePoint(POINT pt)
    {
        RefreshDesktopLayerHandlesIfNeeded();

        IntPtr hwndUnderMouse = User32.WindowFromPoint(pt);
        return hwndUnderMouse != IntPtr.Zero
            && (hwndUnderMouse == _workerWHandle
                || hwndUnderMouse == _desktopListViewHandle
                || hwndUnderMouse == _shellDllDefViewHandle
                || hwndUnderMouse == _overlayHwnd);
    }

    private bool IsForegroundFullscreenAtPoint(POINT pt)
    {
        IntPtr foreground = User32.GetForegroundWindow();
        if (foreground == IntPtr.Zero ||
            foreground == _workerWHandle ||
            foreground == _desktopListViewHandle ||
            foreground == _shellDllDefViewHandle ||
            foreground == _overlayHwnd)
        {
            return false;
        }

        if (!User32.GetWindowRect(foreground, out var rect))
            return false;

        const int tolerance = 64;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            bool pointerOnScreen =
                pt.X >= bounds.Left && pt.X < bounds.Right &&
                pt.Y >= bounds.Top && pt.Y < bounds.Bottom;
            if (!pointerOnScreen)
                continue;

            bool pointerInsideForeground =
                pt.X >= rect.Left && pt.X < rect.Right &&
                pt.Y >= rect.Top && pt.Y < rect.Bottom;
            if (!pointerInsideForeground)
                continue;

            bool coversScreen =
                rect.Left <= bounds.Left + tolerance &&
                rect.Top <= bounds.Top + tolerance &&
                rect.Right >= bounds.Right - tolerance &&
                rect.Bottom >= bounds.Bottom - tolerance;
            if (coversScreen)
                return true;

            int overlapLeft = Math.Max(rect.Left, bounds.Left);
            int overlapTop = Math.Max(rect.Top, bounds.Top);
            int overlapRight = Math.Min(rect.Right, bounds.Right);
            int overlapBottom = Math.Min(rect.Bottom, bounds.Bottom);
            long overlapWidth = Math.Max(0, overlapRight - overlapLeft);
            long overlapHeight = Math.Max(0, overlapBottom - overlapTop);
            long overlapArea = overlapWidth * overlapHeight;
            long screenArea = (long)bounds.Width * bounds.Height;
            if (screenArea > 0 && overlapArea * 100 >= screenArea * 85)
                return true;
        }

        return false;
    }

    private void MouseHook_OnLeftMouseDoubleClick(object? sender, POINT pt)
    {
        bool hasActiveOperation = IsDeskSpaceMouseOperationActive();
        if (!hasActiveOperation && IsForegroundFullscreenAtPoint(pt))
            return;

        if (!hasActiveOperation && !IsDesktopLayerMousePoint(pt))
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            IntPtr hwndUnderMouse = User32.WindowFromPoint(pt);

            // Only respond if the click is actually on the desktop/our overlay
            if (hwndUnderMouse != _workerWHandle && hwndUnderMouse != _desktopListViewHandle &&
                hwndUnderMouse != _overlayHwnd && hwndUnderMouse != _shellDllDefViewHandle)
            {
                return;
            }

            // Check portal spaces first
            var portal = GetPortalAtPoint(pt);
            if (portal != null)
            {
                // Double-click on header (which contains tabs) → rename the clicked tab
                if (IsPointOnPortalHeader(pt, portal))
                {
                    double localX = GetLocalX(pt, portal);
                    int tabIdx = portal.GetTabIndexAtLocalX(localX);
                    if (tabIdx >= 0)
                        portal.StartTabRename(tabIdx);
                    return;
                }

                // Double-click on content → open file
                var canvasPoint = ScreenToCanvas(pt);
                portal.TryOpenFileAt(canvasPoint.X, canvasPoint.Y);
                return;
            }

            var space = GetSpaceAtPoint(pt);
            if (space != null)
            {
                // Double-click on header (which contains tabs) → rename the clicked tab
                if (IsPointOnSpaceHeader(pt, space))
                {
                    double localX = GetLocalX(pt, space);
                    int tabIdx = space.GetTabIndexAtLocalX(localX);
                    if (tabIdx >= 0)
                        space.StartTabRename(tabIdx);
                }
                return;
            }

            // If we reached here, click is on desktop. Check if it's on an icon.
            if (_desktopListViewHandle != IntPtr.Zero)
            {
                int iconIdx = -1;
                
                // If icons are currently quick-hidden, we ignore icon hit-testing 
                // so the double-click always triggers the "show" action.
                if (!_isQuickHidden)
                {
                    iconIdx = ListViewManager.FindIconAtPoint(_desktopListViewHandle, pt.X, pt.Y);
                }

                if (iconIdx < 0)
                {
                    // Double click on empty space (or anywhere if hidden) -> Quick Hide
                    if (_currentSettings.EnableQuickHide)
                    {
                        ToggleQuickHide();
                    }
                }
            }
        }));
    }
    
    private void MouseHook_OnMouseWheel(object? sender, MouseWheelEventData e)
    {
        bool hasActiveOperation = IsDeskSpaceMouseOperationActive();
        if (!hasActiveOperation && IsForegroundFullscreenAtPoint(e.Point))
            return;

        if (!hasActiveOperation && !IsDesktopLayerMousePoint(e.Point))
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            IntPtr hwndUnderMouse = User32.WindowFromPoint(e.Point);
            
            // Only respond if the scroll is actually on the desktop/our overlay
            if (hwndUnderMouse != _workerWHandle && hwndUnderMouse != _desktopListViewHandle && hwndUnderMouse != _overlayHwnd)
            {
                return;
            }

            var portal = GetPortalAtPoint(e.Point);
            if (portal != null)
            {
                portal.ScrollBy(e.Delta);
            }

            var space = GetSpaceAtPoint(e.Point);
            if (space != null)
            {
                space.ScrollBy(e.Delta);
            }
        }));
    }

    private bool MouseHook_OnRightMouseDown(POINT pt)
    {
        bool hasActiveOperation = IsDeskSpaceMouseOperationActive();
        if (!hasActiveOperation && IsForegroundFullscreenAtPoint(pt))
            return false;

        if (!hasActiveOperation && !IsDesktopLayerMousePoint(pt))
            return false;

        bool handled = false;
        Dispatcher.Invoke(() =>
        {
            IntPtr hwndUnderMouse = User32.WindowFromPoint(pt);

            // Only respond if the click is actually on the desktop/our overlay
            if (hwndUnderMouse != _workerWHandle && hwndUnderMouse != _desktopListViewHandle &&
                hwndUnderMouse != _overlayHwnd && hwndUnderMouse != _shellDllDefViewHandle)
            {
                return;
            }

            // Check portals first
            var portal = GetPortalAtPoint(pt);
            if (portal != null)
            {
                if (IsPointOnPortalHeader(pt, portal))
                {
                    portal.ShowContextMenu();
                    handled = true;
                    return;
                }

                var canvasPoint = ScreenToCanvas(pt);
                portal.ShowFileContextMenuAt(canvasPoint.X, canvasPoint.Y);
                handled = true;
                return;
            }

            var space = GetSpaceAtPoint(pt);
            if (space != null)
            {
                if (IsPointOnSpaceHeader(pt, space))
                    space.ShowContextMenu(pt.X, pt.Y);

                handled = true;
            }
        });

        return handled;
    }

    private void CloseAllContextMenus()
    {
        foreach (var child in OverlayCanvas.Children)
        {
            if (child is Controls.PortalSpaceControl portal && portal.IsContextMenuOpen)
                portal.CloseContextMenu();
            else if (child is Controls.SpaceControl space && space.IsContextMenuOpen)
                space.CloseContextMenu();
        }
    }

    private void MouseHook_OnLeftMouseDown(object? sender, POINT pt)
    {
        bool hasActiveOperation = IsDeskSpaceMouseOperationActive();
        if (!hasActiveOperation && IsForegroundFullscreenAtPoint(pt))
            return;

        if (!hasActiveOperation && !IsDesktopLayerMousePoint(pt))
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            IntPtr hwndUnderMouse = User32.WindowFromPoint(pt);

            // Only respond if the click is actually on the desktop/our overlay
            if (hwndUnderMouse != _workerWHandle && hwndUnderMouse != _desktopListViewHandle &&
                hwndUnderMouse != _overlayHwnd && hwndUnderMouse != _shellDllDefViewHandle)
            {
                return;
            }

            // Close any open context menus
            CloseAllContextMenus();

            // Check if clicking on the create space popup first
            if (_createSpacePopup != null)
            {
                var pointOnCanvas = ScreenToCanvas(pt);
                double popupLeft = Canvas.GetLeft(_createSpacePopup);
                double popupTop = Canvas.GetTop(_createSpacePopup);
                
                if (pointOnCanvas.X >= popupLeft && pointOnCanvas.X <= popupLeft + _createSpacePopup.ActualWidth &&
                    pointOnCanvas.Y >= popupTop && pointOnCanvas.Y <= popupTop + _createSpacePopup.ActualHeight)
                {
                    // Click on popup - trigger space creation
                    OnCreateSpaceClicked(null, EventArgs.Empty);
                    return;
                }
                else
                {
                    // Click elsewhere - hide popup
                    HideCreateSpacePopup();
                }
            }
            
            // Check portals first
            var portal = GetPortalAtPoint(pt);
            if (portal != null)
            {
                _isDragging = false;
                _selectionWindow?.HideSelection();

                if (IsPointOnPortalResizeGrip(pt, portal))
                {
                    _activePortal = portal;
                    _isResizingPortal = true;
                    _startPoint = pt;
                    BeginSpaceDrag();
                    return;
                }

                if (IsPointOnPortalMenuButton(pt, portal))
                {
                    portal.ShowContextMenu();
                    return;
                }

                if (IsPointOnPortalHeader(pt, portal))
                {
                    // Check if clicking on a tab or the "+" button
                    double localX = GetLocalX(pt, portal);
                    int tabIdx = portal.GetTabIndexAtLocalX(localX);
                    if (tabIdx == -2)
                    {
                        portal.AddTab();
                        return;
                    }
                    if (tabIdx >= 0 && portal.TabCount > 1)
                    {
                        if (portal.IsCloseButtonHit(localX, tabIdx))
                        {
                            portal.CloseTab(tabIdx);
                            return;
                        }
                        if (tabIdx != portal.ActiveTabIndex)
                        {
                            portal.SwitchToTab(tabIdx);
                            return;
                        }
                    }

                    // No tab action — start move drag
                    _activePortal = portal;
                    _isMovingPortal = true;
                    _startPoint = pt;
                    _unsnappedDragLeft = Canvas.GetLeft(portal);
                    _unsnappedDragTop = Canvas.GetTop(portal);
                    BeginSpaceDrag();
                    return;
                }

                // Check if clicking on the scrollbar track
                {
                    var pointOnCanvas = ScreenToCanvas(pt);
                    double portalLeft = Canvas.GetLeft(portal);
                    double portalTop = Canvas.GetTop(portal);
                    double localX = pointOnCanvas.X - portalLeft;
                    double localY = pointOnCanvas.Y - portalTop;

                    if (portal.IsPointOnScrollbar(localX, localY))
                    {
                        _activePortal = portal;
                        _isScrollingPortal = true;
                        _startPoint = pt;
                        portal.ScrollToFraction(localY);
                        BeginSpaceDrag();
                        return;
                    }
                }

                // Click on portal body — absorb click so it doesn't start selection
                {
                    var canvasPoint = ScreenToCanvas(pt);
                    portal.SelectFileAt(canvasPoint.X, canvasPoint.Y);
                }
                return;
            }

            // Check spaces (before hwnd check since our window is click-through)
            var space = GetSpaceAtPoint(pt);
            if (space != null)
            {
                // Ensure we're not in selection mode
                _isDragging = false;
                _selectionWindow?.HideSelection();
                
                // Check if on resize grip
                if (!space.IsRolledUp && IsPointOnResizeGrip(pt, space))
                {
                    _activeSpace = space;
                    _isResizingSpace = true;
                    _startPoint = pt;
                    BeginSpaceDrag();
                    return;
                }

                // Check if on the menu button (right side of header)
                if (IsPointOnMenuButton(pt, space))
                {
                    space.ShowContextMenu(pt.X, pt.Y);
                    return;
                }

                // Check if on space scrollbar
                {
                    var pointOnCanvas = ScreenToCanvas(pt);
                    double spaceLeft = Canvas.GetLeft(space);
                    double spaceTop = Canvas.GetTop(space);
                    double localX = pointOnCanvas.X - spaceLeft;
                    double localY = pointOnCanvas.Y - spaceTop;

                    if (space.IsPointOnScrollbar(localX, localY))
                    {
                        _activeSpace = space;
                        _isScrollingSpace = true;
                        _startPoint = pt;
                        space.ScrollToFraction(localY);
                        BeginSpaceDrag();
                        return;
                    }
                }

                // Check if on header (which contains tabs)
                if (IsPointOnSpaceHeader(pt, space))
                {
                    // Check if clicking on a tab or the "+" button
                    double localX = GetLocalX(pt, space);
                    int tabIdx = space.GetTabIndexAtLocalX(localX);
                    if (tabIdx == -2)
                    {
                        space.AddTab();
                        return;
                    }
                    if (tabIdx >= 0 && space.TabCount > 1)
                    {
                        if (space.IsCloseButtonHit(localX, tabIdx))
                        {
                            space.CloseTab(tabIdx);
                            return;
                        }
                        if (tabIdx != space.ActiveTabIndex)
                        {
                            space.SwitchToTab(tabIdx);
                            return;
                        }
                    }

                    // No tab action — start move drag
                    _activeSpace = space;
                    _isMovingSpace = true;
                    _startPoint = pt;
                    _dragOrigin = pt;
                    _dragStartSpaceLeft = Canvas.GetLeft(space);
                    _dragStartSpaceTop = Canvas.GetTop(space);
                    _unsnappedDragLeft = _dragStartSpaceLeft;
                    _unsnappedDragTop = _dragStartSpaceTop;
                    _activeSpace.BeginMoveIcons();
                    BeginSpaceDrag();
                    return;
                }

                // Click on space body — check if clicking on an icon inside the space
                if (_desktopListViewHandle != IntPtr.Zero)
                {
                    int iconIdx = ListViewManager.FindIconAtPoint(_desktopListViewHandle, pt.X, pt.Y);
                    if (iconIdx >= 0)
                    {
                        // Grab all selected icons (multi-select), or just the clicked one
                        var selected = ListViewManager.GetSelectedIndices(_desktopListViewHandle);
                        if (selected.Count > 1 && selected.Contains(iconIdx))
                            _draggedIconIndices = selected;
                        else
                            _draggedIconIndices = new List<int> { iconIdx };

                        _isDraggingIcon = true;
                        _dragSourceSpace = space;
                        _dropTargetSpace = null;
                        return; // Let Windows handle the visual drag
                    }
                }
                return;
            }
            
            // Check if clicking on an icon - track it for drag-into-space
            if (_desktopListViewHandle != IntPtr.Zero)
            {
                int iconIdx = ListViewManager.FindIconAtPoint(_desktopListViewHandle, pt.X, pt.Y);
                if (iconIdx >= 0)
                {
                    // Grab all selected icons (multi-select), or just the clicked one
                    var selected = ListViewManager.GetSelectedIndices(_desktopListViewHandle);
                    if (selected.Count > 1 && selected.Contains(iconIdx))
                        _draggedIconIndices = selected;
                    else
                        _draggedIconIndices = new List<int> { iconIdx };

                    _isDraggingIcon = true;
                    _dragSourceSpace = FindOwnerSpace(iconIdx);
                    _dropTargetSpace = null;
                    return; // Let Windows handle the visual drag
                }
            }
            
            // Start selection rect for new space
            _isDragging = true;
            _startPoint = pt;
            _selectionWindow?.ShowSelection(pt.X, pt.Y, 0, 0);
        }));
    }

    private bool _isProcessingMouseMove = false;
    private long _lastAmbientMouseMoveTicks = 0;
    private long _suppressAmbientMouseUntilTicks = 0;
    private const long AmbientMouseMoveThrottleMs = 500;
    private const long FullscreenAmbientSuppressMs = 3000;

    private bool NeedsAmbientMouseState()
    {
        return _currentSettings.ZenModeEnabled ||
               (_currentSettings.EnableQuickHide &&
                (_currentSettings.QuickHideAutoHide || _currentSettings.QuickHideAutoShow));
    }

    private void MouseHook_OnMouseMove(object? sender, POINT pt)
    {
        bool hasActiveOperation = IsDeskSpaceMouseOperationActive();
        long now = Environment.TickCount64;

        if (!hasActiveOperation)
        {
            if (!NeedsAmbientMouseState())
                return;

            if (now < _suppressAmbientMouseUntilTicks)
                return;

            if (now - _lastAmbientMouseMoveTicks < AmbientMouseMoveThrottleMs)
                return;

            _lastAmbientMouseMoveTicks = now;

            if (IsForegroundFullscreenAtPoint(pt))
            {
                _suppressAmbientMouseUntilTicks = now + FullscreenAmbientSuppressMs;
                return;
            }
        }

        bool isDesktopPoint = hasActiveOperation || IsDesktopLayerMousePoint(pt);
        bool ambientOnly = !hasActiveOperation && !isDesktopPoint;

        if (ambientOnly && !NeedsAmbientMouseState())
        {
            return;
        }

        if (_isProcessingMouseMove) return;
        _isProcessingMouseMove = true;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                // ZenMode: evaluate activity near the desktop layer on every move.
                UpdateZenModeState(pt);
                
                // Quick Hide: evaluate auto-hide/show activity.
                UpdateQuickHideAutoState(pt);

                if (ambientOnly)
                    return;

                // Highlight drop target while dragging an icon
                if (_isDraggingIcon && _draggedIconIndices.Count > 0)
                {
                    var target = GetSpaceAtPoint(pt);
                    if (target != _dropTargetSpace)
                    {
                        _dropTargetSpace?.HideDropHighlight();
                        _dropTargetSpace = target;
                        _dropTargetSpace?.ShowDropHighlight();
                    }
                    return; // Don't interfere with Windows' native icon drag
                }

                // Portal scrollbar drag
                if (_isScrollingPortal && _activePortal != null)
                {
                    var pointOnCanvas = ScreenToCanvas(pt);
                    double portalTop = Canvas.GetTop(_activePortal);
                    double localY = pointOnCanvas.Y - portalTop;
                    _activePortal.ScrollToFraction(localY);
                    return;
                }

                // Space scrollbar drag
                if (_isScrollingSpace && _activeSpace != null)
                {
                    var pointOnCanvas = ScreenToCanvas(pt);
                    double spaceTop = Canvas.GetTop(_activeSpace);
                    double localY = pointOnCanvas.Y - spaceTop;
                    _activeSpace.ScrollToFraction(localY);
                    return;
                }

                // Portal move/resize
                if (_isMovingPortal && _activePortal != null)
                {
                    double deltaX = (pt.X - _startPoint.X) / _dpiScaleX;
                    double deltaY = (pt.Y - _startPoint.Y) / _dpiScaleY;
                    _unsnappedDragLeft += deltaX;
                    _unsnappedDragTop += deltaY;
                    double newLeft = _unsnappedDragLeft;
                    double newTop = _unsnappedDragTop;
                    double w = _activePortal.ActualWidth > 0 ? _activePortal.ActualWidth : _activePortal.Width;
                    double h = _activePortal.ActualHeight > 0 ? _activePortal.ActualHeight : _activePortal.Height;
                    SnapPosition(_activePortal, ref newLeft, ref newTop, w, h);
                    Canvas.SetLeft(_activePortal, newLeft);
                    Canvas.SetTop(_activePortal, newTop);
                    _startPoint = pt;
                    return;
                }

            if (_isResizingPortal && _activePortal != null)
            {
                double deltaX = (pt.X - _startPoint.X) / _dpiScaleX;
                double deltaY = (pt.Y - _startPoint.Y) / _dpiScaleY;
                double newWidth = _activePortal.Width + deltaX;
                double newHeight = _activePortal.Height + deltaY;
                if (newWidth >= 150) _activePortal.Width = newWidth;
                if (newHeight >= 100) _activePortal.Height = newHeight;
                _startPoint = pt;
                return;
            }

            if (_isMovingSpace && _activeSpace != null)
            {
                double deltaX = (pt.X - _startPoint.X) / _dpiScaleX;
                double deltaY = (pt.Y - _startPoint.Y) / _dpiScaleY;

                _unsnappedDragLeft += deltaX;
                _unsnappedDragTop += deltaY;
                double newLeft = _unsnappedDragLeft;
                double newTop = _unsnappedDragTop;
                double w = _activeSpace.ActualWidth > 0 ? _activeSpace.ActualWidth : _activeSpace.Width;
                double h = _activeSpace.ActualHeight > 0 ? _activeSpace.ActualHeight : _activeSpace.Height;
                SnapPosition(_activeSpace, ref newLeft, ref newTop, w, h);

                Canvas.SetLeft(_activeSpace, newLeft);
                Canvas.SetTop(_activeSpace, newTop);

                _startPoint = pt;

                // Compute icon offset from the actual (snapped) canvas position change.
                // Using raw mouse delta (pt - _dragOrigin) breaks when SnapPosition clamps
                // the space at a screen edge — icons would drift outside the space.
                double totalDX = (newLeft - _dragStartSpaceLeft) * _dpiScaleX;
                double totalDY = (newTop - _dragStartSpaceTop) * _dpiScaleY;
                _activeSpace.ApplyMoveOffset(totalDX, totalDY);
                return;
            }

            if (_isResizingSpace && _activeSpace != null)
            {
                double deltaX = (pt.X - _startPoint.X) / _dpiScaleX;
                double deltaY = (pt.Y - _startPoint.Y) / _dpiScaleY;

                double newWidth = _activeSpace.Width + deltaX;
                double newHeight = _activeSpace.Height + deltaY;

                if (newWidth >= 100) _activeSpace.Width = newWidth;
                if (newHeight >= 80) _activeSpace.Height = newHeight;

                _startPoint = pt;
                _activeSpace.UpdateCapturedIconsPositions();
                return;
            }
            
            if (_isDragging)
            {
                double x = Math.Min(pt.X, _startPoint.X) / _dpiScaleX;
                double y = Math.Min(pt.Y, _startPoint.Y) / _dpiScaleY;
                double width = Math.Abs(pt.X - _startPoint.X) / _dpiScaleX;
                double height = Math.Abs(pt.Y - _startPoint.Y) / _dpiScaleY;

                _selectionWindow?.ShowSelection(x, y, width, height);
            }
            }
            finally
            {
                _isProcessingMouseMove = false;
            }
        }));
    }

    private void MouseHook_OnLeftMouseUp(object? sender, POINT pt)
    {
        bool hasActiveOperation = IsDeskSpaceMouseOperationActive();
        if (!hasActiveOperation && IsForegroundFullscreenAtPoint(pt))
            return;

        if (!hasActiveOperation && !IsDesktopLayerMousePoint(pt))
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            // Handle icon drop into space
            if (_isDraggingIcon)
            {
                _isDraggingIcon = false;
                _dropTargetSpace?.HideDropHighlight();

                var iconIndices = new List<int>(_draggedIconIndices);
                var source = _dragSourceSpace;
                var target = GetSpaceAtPoint(pt);

                _draggedIconIndices.Clear();
                _dragSourceSpace = null;
                _dropTargetSpace = null;

                // Pre-set save guard so hot-reload ignores the upcoming save
                _lastSaveTime = DateTime.UtcNow;

                // Immediately rehide inactive tabs and start continuous guard —
                // Explorer may reposition hidden icons at any point during/after drop.
                RehideAllInactiveTabs();
                StartRehideGuard();

                // Delay processing so Windows can finalize its native icon drop first,
                // then we snap the icons into the space grid (overriding Windows' positions).
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(150)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();

                    foreach (int iconIdx in iconIndices)
                    {
                        if (target != null)
                        {
                            // Remove from source space if it was in one
                            var owner = FindOwnerSpace(iconIdx);
                            if (owner != null && owner != target)
                                owner.RemoveIcon(iconIdx);

                            // Accept into target space (snaps to grid)
                            target.AcceptDroppedIcon(iconIdx);
                        }
                        else if (source != null)
                        {
                            // Dragged OUT of a space onto free desktop — remove from source
                            source.RemoveIcon(iconIdx);
                        }
                    }

                    RehideAllInactiveTabs();
                    SaveSpaces();

                    // Explorer may reposition icons well after the drop completes.
                    // Fire repeated rehide bursts to catch delayed Explorer layout refreshes.
                    StartRehideGuard();
                };
                timer.Start();
                return;
            }

            // Handle portal scrollbar drag end
            if (_isScrollingPortal)
            {
                EndSpaceDrag();
                _isScrollingPortal = false;
                _activePortal = null;
                return;
            }

            // Handle portal move/resize end
            if (_isMovingPortal || _isResizingPortal)
            {
                EndSpaceDrag();
                _isMovingPortal = false;
                _isResizingPortal = false;
                _activePortal = null;
                SavePortals();
                return;
            }

            // Handle space move/resize end
            if (_isMovingSpace || _isResizingSpace)
            {
                EndSpaceDrag();
                if (_activeSpace != null)
                {
                    _activeSpace.EndMoveIcons();
                    _activeSpace.UpdateCapturedIconsPositions();
                    RehideAllInactiveTabs();

                    if (_isMovingSpace)
                    {
                        var dropTarget = GetSpaceAtPoint(pt);
                        if (dropTarget != null && dropTarget != _activeSpace)
                        {
                            dropTarget.MergeSpace(_activeSpace);
                            OverlayCanvas.Children.Remove(_activeSpace);
                            _activeSpace = null;
                        }
                    }
                }
                _isMovingSpace = false;
                _isResizingSpace = false;
                _activeSpace = null;
                SaveSpaces();
                return;
            }
            
            if (!_isDragging) return;

            try
            {
                _isDragging = false;
                
                double x = Math.Min(pt.X, _startPoint.X) / _dpiScaleX;
                double y = Math.Min(pt.Y, _startPoint.Y) / _dpiScaleY;
                double w = Math.Abs(pt.X - _startPoint.X) / _dpiScaleX;
                double h = Math.Abs(pt.Y - _startPoint.Y) / _dpiScaleY;

                _selectionWindow?.HideSelection();

                // Check if the selection is large enough to be a space
                if (w > 50 && h > 50)
                {
                    // Store pending space dimensions (already in DIPs)
                    _pendingSpaceX = x - this.Left;
                    _pendingSpaceY = y - this.Top;
                    _pendingSpaceW = w;
                    _pendingSpaceH = h;
                    
                    // Remove any existing popup
                    if (_createSpacePopup != null)
                    {
                        OverlayCanvas.Children.Remove(_createSpacePopup);
                    }
                    
                    // Show the "Create Space" popup at the bottom of the selection
                    _createSpacePopup = new Controls.CreateSpacePopup();
                    _createSpacePopup.CreateSpaceClicked += OnCreateSpaceClicked;
                    
                    Canvas.SetLeft(_createSpacePopup, _pendingSpaceX);
                    Canvas.SetTop(_createSpacePopup, _pendingSpaceY + _pendingSpaceH + 5);
                    
                    OverlayCanvas.Children.Add(_createSpacePopup);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error in MouseUp: {ex.Message}\n\n{ex.StackTrace}", "DeskSpace OS Error");
                _selectionWindow?.HideSelection();
            }
        }));
    }
    
    private void OnCreateSpaceClicked(object? sender, EventArgs e)
    {
        // Remove the popup
        if (_createSpacePopup != null)
        {
            _createSpacePopup.CreateSpaceClicked -= OnCreateSpaceClicked;
            OverlayCanvas.Children.Remove(_createSpacePopup);
            _createSpacePopup = null;
        }

        CreateSpace(_pendingSpaceX, _pendingSpaceY, _pendingSpaceW, _pendingSpaceH, captureIcons: true);
    }

    private void CreateSpaceAtCursor()
    {
        const double defaultWidth = 320;
        const double defaultHeight = 220;

        var cursor = System.Windows.Forms.Cursor.Position;
        double x = cursor.X / _dpiScaleX - this.Left - defaultWidth / 2;
        double y = cursor.Y / _dpiScaleY - this.Top - defaultHeight / 2;

        x = Math.Clamp(x, 0, Math.Max(0, this.ActualWidth - defaultWidth));
        y = Math.Clamp(y, 0, Math.Max(0, this.ActualHeight - defaultHeight));

        CreateSpace(x, y, defaultWidth, defaultHeight, captureIcons: false);
    }

    private void CreateSpace(double x, double y, double width, double height, bool captureIcons)
    {
        var spaceControl = new Controls.SpaceControl
        {
            Width = width,
            Height = height,
            Title = "New Space",
            Visibility = Visibility.Visible
        };
        spaceControl.ApplyAppearance(
            _currentSettings.DefaultColorR,
            _currentSettings.DefaultColorG,
            _currentSettings.DefaultColorB,
            _currentSettings.DefaultAlpha);

        Canvas.SetLeft(spaceControl, x);
        Canvas.SetTop(spaceControl, y);

        RegisterSpace(spaceControl);
        OverlayCanvas.Children.Add(spaceControl);

        if (captureIcons)
        {
            try
            {
                spaceControl.CaptureIconsWithinBounds();
            }
            catch (Exception exInner)
            {
                System.Windows.MessageBox.Show($"Error in CaptureIconsWithinBounds: {exInner.Message}", "DeskSpace OS Error");
            }
        }

        SaveSpaces();
    }

    private void RegisterSpace(Controls.SpaceControl space)
    {
        space.StateChanged += (_, _) => SaveSpaces();
        space.Deleted += (_, _) => SaveSpaces();
    }

    private void SaveSpaces()
    {
        try
        {
            _lastSaveTime = DateTime.UtcNow;
            var models = new List<Space>();
            foreach (var child in OverlayCanvas.Children)
            {
                if (child is Controls.SpaceControl cc)
                    models.Add(cc.ToModel());
            }
            SpaceStore.Save(models);
        }
        catch { /* Don't crash if save fails */ }
    }

    /// <summary>Re-hide inactive tab icons on all spaces.</summary>
    private void RehideAllInactiveTabs()
    {
        foreach (var child in OverlayCanvas.Children)
        {
            if (child is Controls.SpaceControl cc)
                cc.RehideInactiveTabIcons();
        }
    }

    private System.Windows.Threading.DispatcherTimer? _rehideTimer;
    private int _rehideTicksRemaining;

    /// <summary>
    /// Start a high-frequency continuous rehide timer (~30ms interval, runs for ~1.5s).
    /// Any Explorer repositioning is caught within a single display frame.
    /// If already running, resets the counter so the guard stays active.
    /// </summary>
    private void StartRehideGuard()
    {
        _rehideTicksRemaining = 50; // 50 × 30ms ≈ 1.5s

        if (_rehideTimer != null)
            return; // already running, just reset the counter

        _rehideTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _rehideTimer.Tick += (_, _) =>
        {
            RehideAllInactiveTabs();
            _lastSaveTime = DateTime.UtcNow; // keep save guard fresh

            if (--_rehideTicksRemaining <= 0)
            {
                _rehideTimer!.Stop();
                _rehideTimer = null;
            }
        };
        _rehideTimer.Start();
    }

    private void LoadSpaces()
    {
        try
        {
            var models = SpaceStore.Load();
            foreach (var model in models)
            {
                var cc = new Controls.SpaceControl
                {
                    Width = model.Width,
                    Height = model.Height,
                    Visibility = Visibility.Visible
                };
                cc.ApplyModel(model);

                Canvas.SetLeft(cc, model.X);
                Canvas.SetTop(cc, model.Y);

                RegisterSpace(cc);
                OverlayCanvas.Children.Add(cc);

                cc.RestoreAllTabIcons(model);
            }
        }
        catch { /* Don't crash if load fails */ }
    }
    
    private IntPtr _overlayHwnd = IntPtr.Zero;

    /// <summary>
    /// Cancels any native desktop selection rectangle and makes the overlay
    /// non-transparent so subsequent mouse messages don't reach the desktop.
    /// </summary>
    private void BeginSpaceDrag()
    {
        if (_overlayHwnd == IntPtr.Zero)
            _overlayHwnd = new WindowInteropHelper(this).Handle;

        // Cancel the desktop ListView's active mouse tracking
        IntPtr listView = DesktopManager.GetDesktopListViewHandle();
        if (listView != IntPtr.Zero)
        {
            const uint WM_CANCELMODE = 0x001F;
            User32.SendMessage(listView, WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
        }

        // Remove WS_EX_TRANSPARENT so the overlay blocks mouse events from reaching the desktop
        int exStyle = User32.GetWindowLong(_overlayHwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLong(_overlayHwnd, User32.GWL_EXSTYLE, exStyle & ~User32.WS_EX_TRANSPARENT);

        // Temporarily allow the overlay to float above other windows during the drag
        _isDraggingElement = true;
    }

    /// <summary>
    /// Restores WS_EX_TRANSPARENT and re-pins to desktop layer.
    /// </summary>
    private void EndSpaceDrag()
    {
        if (_overlayHwnd == IntPtr.Zero)
            return;

        // Restore click-through
        int exStyle = User32.GetWindowLong(_overlayHwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLong(_overlayHwnd, User32.GWL_EXSTYLE, exStyle | User32.WS_EX_TRANSPARENT);

        // Re-pin to desktop layer (above WorkerW, behind apps) — clear flag AFTER pin
        // so WndProc doesn't interfere with the SetWindowPos call
        User32.SetWindowPos(_overlayHwnd, _workerWHandle,
            0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE | User32.SWP_NOSENDCHANGING);
        _isDraggingElement = false;
    }

    /// <summary>
    /// Places a window just above WorkerW in the z-order so it appears
    /// on the desktop but behind all normal application windows.
    /// </summary>
    private void PinToDesktopLayer(IntPtr hwnd)
    {
        if (_workerWHandle != IntPtr.Zero)
        {
            User32.SetWindowPos(hwnd, _workerWHandle,
                0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
        }
    }

    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int HOTKEY_ID_PEEK = 1;
    private const int HOTKEY_ID_QUICKHIDE = 2;
    private const int HOTKEY_ID_NEW_SPACE = 3;

    private void RegisterHotkeys(IntPtr hwnd)
    {
        var settings = AppSettingsStore.Load();
        if (settings.EnablePeekMode)
            RegisterSingleHotkey(hwnd, HOTKEY_ID_PEEK, settings.PeekModeHotkey);
        RegisterSingleHotkey(hwnd, HOTKEY_ID_QUICKHIDE, settings.DistractionFreeHotkey);
        RegisterSingleHotkey(hwnd, HOTKEY_ID_NEW_SPACE, settings.NewSpaceHotkey);
    }

    private void RegisterSingleHotkey(IntPtr hwnd, int id, string hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString)) return;

        uint modifiers = 0;
        uint vk = 0;

        var parts = hotkeyString.Split('+', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var part = p.Trim().ToUpper();
            if (part == "WIN") modifiers |= User32.MOD_WIN;
            else if (part == "CTRL" || part == "CONTROL") modifiers |= User32.MOD_CONTROL;
            else if (part == "ALT") modifiers |= User32.MOD_ALT;
            else if (part == "SHIFT") modifiers |= User32.MOD_SHIFT;
            else
            {
                // Simple parsing for A-Z, 0-9, Space
                if (part == "SPACE") vk = 0x20;
                else if (part.Length == 1) vk = (uint)part[0];
                // Could expand to other keys if needed
            }
        }

        if (vk != 0)
        {
            // Unregister first in case it's a reload
            User32.UnregisterHotKey(hwnd, id);
            // MOD_NOREPEAT prevents spamming if key is held down
            User32.RegisterHotKey(hwnd, id, modifiers | User32.MOD_NOREPEAT, vk);
        }
    }

    private void UnregisterHotkeys(IntPtr hwnd)
    {
        User32.UnregisterHotKey(hwnd, HOTKEY_ID_PEEK);
        User32.UnregisterHotKey(hwnd, HOTKEY_ID_QUICKHIDE);
        User32.UnregisterHotKey(hwnd, HOTKEY_ID_NEW_SPACE);
    }

    private bool _isPeekMode = false;

    private void TogglePeekMode()
    {
        _isPeekMode = !_isPeekMode;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        if (_isPeekMode)
        {
            // Bring to very top
            User32.SetWindowPos(hwnd, User32.HWND_TOPMOST,
                0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
        }
        else
        {
            // Put back to desktop layer
            User32.SetWindowPos(hwnd, User32.HWND_NOTOPMOST,
                0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
            PinToDesktopLayer(hwnd);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ID_PEEK)
            {
                TogglePeekMode();
                handled = true;
            }
            else if (id == HOTKEY_ID_QUICKHIDE)
            {
                ToggleQuickHide();
                handled = true;
            }
            else if (id == HOTKEY_ID_NEW_SPACE)
            {
                CreateSpaceAtCursor();
                handled = true;
            }
        }
        else if (msg == WM_WINDOWPOSCHANGING && _workerWHandle != IntPtr.Zero && !_isDraggingElement && !_isPeekMode)
        {
            // Keep us pinned just above WorkerW — prevent Windows from
            // raising us above application windows on activation/click.
            // WINDOWPOS struct: hwnd(IntPtr), hwndInsertAfter(IntPtr), x, y, cx, cy, flags
            int ptrSize = IntPtr.Size;
            IntPtr afterField = System.Runtime.InteropServices.Marshal.ReadIntPtr(lParam, ptrSize);

            // Only intervene if Windows is trying to bring us higher than WorkerW
            if (afterField != _workerWHandle)
            {
                System.Runtime.InteropServices.Marshal.WriteIntPtr(lParam, ptrSize, _workerWHandle);
            }
        }
        return IntPtr.Zero;
    }

    private void HideCreateSpacePopup()
    {
        if (_createSpacePopup != null)
        {
            _createSpacePopup.CreateSpaceClicked -= OnCreateSpaceClicked;
            OverlayCanvas.Children.Remove(_createSpacePopup);
            _createSpacePopup = null;
        }
    }

    /// <summary>
    /// Finds the space that currently owns the given icon index, or null.
    /// </summary>
    private Controls.SpaceControl? FindOwnerSpace(int iconIndex)
    {
        foreach (var child in OverlayCanvas.Children)
        {
            if (child is Controls.SpaceControl cc && cc.ContainsIcon(iconIndex))
                return cc;
        }
        return null;
    }

    // --- Folder Portals ---

    private Controls.PortalSpaceControl? GetPortalAtPoint(POINT pt)
    {
        var pointOnCanvas = ScreenToCanvas(pt);

        foreach (var child in OverlayCanvas.Children)
        {
            if (child is Controls.PortalSpaceControl portal)
            {
                double left = Canvas.GetLeft(portal);
                double top = Canvas.GetTop(portal);
                double width = portal.ActualWidth > 0 ? portal.ActualWidth : portal.Width;
                double height = portal.ActualHeight > 0 ? portal.ActualHeight : portal.Height;

                if (pointOnCanvas.X >= left && pointOnCanvas.X <= left + width &&
                    pointOnCanvas.Y >= top && pointOnCanvas.Y <= top + height)
                {
                    return portal;
                }
            }
        }
        return null;
    }

    private void LoadPortals()
    {
        try
        {
            var portals = FolderPortalStore.Load();
            foreach (var model in portals)
            {
                var pc = new Controls.PortalSpaceControl
                {
                    Width = model.Width,
                    Height = model.Height,
                    Visibility = Visibility.Visible
                };
                pc.ApplyModel(model);

                Canvas.SetLeft(pc, model.X);
                Canvas.SetTop(pc, model.Y);

                pc.StateChanged += (_, _) => SavePortals();
                pc.Deleted += (_, _) =>
                {
                    _portalWatcher.Unwatch(pc.PortalId);
                    SavePortals();
                };
                pc.TabSwitched += (_, _) =>
                {
                    _portalWatcher.Rewatch(pc);
                    SavePortals();
                };

                OverlayCanvas.Children.Add(pc);

                // Initial scan + start watching
                pc.RefreshFiles();
                _portalWatcher.Watch(pc);
            }
        }
        catch { /* Don't crash if load fails */ }
    }

    private void SavePortals()
    {
        try
        {
            _lastSaveTime = DateTime.UtcNow;
            var models = new List<FolderPortal>();
            foreach (var child in OverlayCanvas.Children)
            {
                if (child is Controls.PortalSpaceControl pc)
                    models.Add(pc.ToModel());
            }
            FolderPortalStore.Save(models);
        }
        catch { }
    }

    // --- Hot-reload handlers ---

    private bool ShouldIgnoreFileChange =>
        (DateTime.UtcNow - _lastSaveTime).TotalMilliseconds < SaveGuardMs
        || _isDraggingElement
        || _isDraggingIcon
        || _isMovingSpace || _isResizingSpace
        || _isMovingPortal || _isResizingPortal;

    private void OnSpacesFileChanged(object? sender, EventArgs e)
    {
        if (ShouldIgnoreFileChange) return;
        Dispatcher.Invoke(() =>
        {
            try
            {
                var newModels = SpaceStore.Load();
                var existingById = new Dictionary<Guid, Controls.SpaceControl>();
                foreach (var child in OverlayCanvas.Children)
                {
                    if (child is Controls.SpaceControl cc)
                        existingById[cc.SpaceId] = cc;
                }

                var newIds = new HashSet<Guid>(newModels.Select(m => m.Id));

                // Remove spaces that were deleted in settings
                foreach (var kvp in existingById)
                {
                    if (!newIds.Contains(kvp.Key))
                    {
                        OverlayCanvas.Children.Remove(kvp.Value);
                        _logger?.LogInformation("Hot-reload: removed space {Id}", kvp.Key);
                    }
                }

                // Update existing or add new spaces
                foreach (var model in newModels)
                {
                    if (existingById.TryGetValue(model.Id, out var existing))
                    {
                        // Update visual properties (title, color, size) but keep icon state
                        existing.ApplyModel(model);
                        Canvas.SetLeft(existing, model.X);
                        Canvas.SetTop(existing, model.Y);
                    }
                    else
                    {
                        // New space added from settings app
                        var cc = new Controls.SpaceControl
                        {
                            Width = model.Width,
                            Height = model.Height,
                            Visibility = Visibility.Visible
                        };
                        cc.ApplyModel(model);
                        Canvas.SetLeft(cc, model.X);
                        Canvas.SetTop(cc, model.Y);
                        RegisterSpace(cc);
                        OverlayCanvas.Children.Add(cc);
                        cc.RestoreAllTabIcons(model);
                        _logger?.LogInformation("Hot-reload: added space {Id}", model.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to hot-reload spaces");
            }
        });
    }

    private void OnPortalsFileChanged(object? sender, EventArgs e)
    {
        if (ShouldIgnoreFileChange) return;
        Dispatcher.Invoke(() =>
        {
            try
            {
                var newModels = FolderPortalStore.Load();
                var existingById = new Dictionary<Guid, Controls.PortalSpaceControl>();
                foreach (var child in OverlayCanvas.Children)
                {
                    if (child is Controls.PortalSpaceControl pc)
                        existingById[pc.PortalId] = pc;
                }

                var newIds = new HashSet<Guid>(newModels.Select(m => m.Id));

                // Remove portals that were deleted
                foreach (var kvp in existingById)
                {
                    if (!newIds.Contains(kvp.Key))
                    {
                        _portalWatcher.Unwatch(kvp.Key);
                        OverlayCanvas.Children.Remove(kvp.Value);
                        _logger?.LogInformation("Hot-reload: removed portal {Id}", kvp.Key);
                    }
                }

                // Update existing or add new portals
                foreach (var model in newModels)
                {
                    if (existingById.TryGetValue(model.Id, out var existing))
                    {
                        existing.ApplyModel(model);
                        Canvas.SetLeft(existing, model.X);
                        Canvas.SetTop(existing, model.Y);
                    }
                    else
                    {
                        var pc = new Controls.PortalSpaceControl
                        {
                            Width = model.Width,
                            Height = model.Height,
                            Visibility = Visibility.Visible
                        };
                        pc.ApplyModel(model);
                        Canvas.SetLeft(pc, model.X);
                        Canvas.SetTop(pc, model.Y);

                        pc.StateChanged += (_, _) => SavePortals();
                        pc.Deleted += (_, _) =>
                        {
                            _portalWatcher.Unwatch(pc.PortalId);
                            SavePortals();
                        };
                        pc.TabSwitched += (_, _) =>
                        {
                            _portalWatcher.Rewatch(pc);
                            SavePortals();
                        };

                        OverlayCanvas.Children.Add(pc);
                        pc.RefreshFiles();
                        _portalWatcher.Watch(pc);
                        _logger?.LogInformation("Hot-reload: added portal {Id}", model.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to hot-reload portals");
            }
        });
    }

    public event EventHandler? AppSettingsReloaded;

    private void OnSettingsFileChanged(object? sender, EventArgs e)
    {
        // Hot-reload local settings copy
        _currentSettings = AppSettingsStore.Load();

        Dispatcher.BeginInvoke(new Action(RefreshTabStyles));
        Dispatcher.BeginInvoke(new Action(RefreshHotkeys));

        // Forward to anyone who needs it (Worker for DisableAutoArrange, etc.)
        AppSettingsReloaded?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshHotkeys()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        UnregisterHotkeys(hwnd);
        RegisterHotkeys(hwnd);
    }

    private void RefreshTabStyles()
    {
        foreach (var child in OverlayCanvas.Children)
        {
            if (child is Controls.SpaceControl space)
                space.RefreshTabStyle();
            else if (child is Controls.PortalSpaceControl portal)
                portal.RefreshTabStyle();
        }
    }

    // --- Sorting Rules (Phase 5) ---

    private void OnSortingRulesFileChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { _sortingRules = SortingRuleStore.Load(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to reload sorting rules"); }
        }));
    }

    private void OnDesktopFileArrived(object? sender, string fullPath)
    {
        // Marshal to UI thread — ListView calls must be serialized with other overlay work.
        Dispatcher.BeginInvoke(new Action(() => TryApplyRuleToFile(fullPath, attempt: 0)));
    }

    /// <summary>
    /// Look up the newly created desktop item in the ListView and place it into the
    /// first matching space. Explorer takes a moment to register new items, so we
    /// retry a handful of times with an increasing delay.
    /// </summary>
    private void TryApplyRuleToFile(string fullPath, int attempt)
    {
        if (_sortingRules.Count == 0) return;

        var rule = SortingRuleEvaluator.FindMatch(_sortingRules, fullPath);
        if (rule == null) return;

        var space = FindSpaceForRule(rule);
        if (space == null) return;

        IntPtr lv = DesktopManager.GetDesktopListViewHandle();
        if (lv == IntPtr.Zero) return;

        string fileName = System.IO.Path.GetFileName(fullPath);
        int iconIdx = ListViewManager.FindItemByName(lv, fileName);
        if (iconIdx < 0)
        {
            // Try without extension (Explorer may hide known extensions)
            string withoutExt = System.IO.Path.GetFileNameWithoutExtension(fullPath);
            if (!string.IsNullOrEmpty(withoutExt) && withoutExt != fileName)
                iconIdx = ListViewManager.FindItemByName(lv, withoutExt);
        }

        if (iconIdx < 0)
        {
            if (attempt >= 10) return; // give up after ~5s
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            timer.Tick += (_, _) => { timer.Stop(); TryApplyRuleToFile(fullPath, attempt + 1); };
            timer.Start();
            return;
        }

        if (!space.ContainsIcon(iconIdx))
        {
            space.AcceptDroppedIcon(iconIdx);
            _logger?.LogInformation("Sorted new desktop item '{File}' into space '{Space}'",
                fileName, space.Title);
            SaveSpaces();
        }
    }

    private Controls.SpaceControl? FindSpaceForRule(SortingRule rule)
    {
        foreach (var child in OverlayCanvas.Children)
        {
            if (child is not Controls.SpaceControl cc) continue;
            if (rule.TargetSpaceId != Guid.Empty && cc.SpaceId == rule.TargetSpaceId)
                return cc;
        }
        // Fall back to title match (legacy rules)
        foreach (var child in OverlayCanvas.Children)
        {
            if (child is Controls.SpaceControl cc
                && string.Equals(cc.Title, rule.TargetSpaceTitle, StringComparison.OrdinalIgnoreCase))
                return cc;
        }
        return null;
    }

    // --- Snapping (Phase 6) ---

    /// <summary>
    /// Adjust the target top-left so the element snaps to screen edges or other spaces/portals
    /// within the user-configured snap threshold. Returns immediately if snapping is disabled.
    /// </summary>
    private void SnapPosition(UIElement active, ref double left, ref double top, double width, double height)
    {
        var settings = AppSettingsStore.Load();
        if (!settings.SnappingEnabled) return;

        double threshold = settings.SnapThresholdDIPs;
        if (threshold <= 0) return;

        double right = left + width;
        double bottom = top + height;

        // Screen edges (virtual screen)
        double screenLeft = 0;
        double screenTop = 0;
        double screenRight = SystemParameters.VirtualScreenWidth;
        double screenBottom = SystemParameters.VirtualScreenHeight;

        if (Math.Abs(left - screenLeft) <= threshold) left = screenLeft;
        else if (Math.Abs(right - screenRight) <= threshold) left = screenRight - width;

        if (Math.Abs(top - screenTop) <= threshold) top = screenTop;
        else if (Math.Abs(bottom - screenBottom) <= threshold) top = screenBottom - height;

        // Neighboring spaces/portals
        foreach (var child in OverlayCanvas.Children)
        {
            if (!(child is Controls.SpaceControl) && !(child is Controls.PortalSpaceControl))
                continue;
            if (ReferenceEquals(child, active)) continue;

            var fe = (FrameworkElement)child;
            double oLeft = Canvas.GetLeft(fe);
            double oTop = Canvas.GetTop(fe);
            if (double.IsNaN(oLeft) || double.IsNaN(oTop)) continue;
            double oW = fe.ActualWidth > 0 ? fe.ActualWidth : fe.Width;
            double oH = fe.ActualHeight > 0 ? fe.ActualHeight : fe.Height;
            double oRight = oLeft + oW;
            double oBottom = oTop + oH;

            // Align our left edge to their left or right
            if (Math.Abs(left - oLeft) <= threshold) left = oLeft;
            else if (Math.Abs(left - oRight) <= threshold) left = oRight;
            else if (Math.Abs(left + width - oLeft) <= threshold) left = oLeft - width;
            else if (Math.Abs(left + width - oRight) <= threshold) left = oRight - width;

            // Align our top edge to their top or bottom
            if (Math.Abs(top - oTop) <= threshold) top = oTop;
            else if (Math.Abs(top - oBottom) <= threshold) top = oBottom;
            else if (Math.Abs(top + height - oTop) <= threshold) top = oTop - height;
            else if (Math.Abs(top + height - oBottom) <= threshold) top = oBottom - height;
        }
    }

    // --- ZenMode Mode (Phase 6) ---

    private void UpdateZenModeState(POINT pt)
    {
        if (!_currentSettings.ZenModeEnabled)
        {
            // If the setting was just turned off, restore full opacity immediately.
            if (_zenModeFaded || OverlayCanvas.Opacity < 1.0)
            {
                _zenModeFaded = false;
                AnimateOverlayOpacity(1.0);
            }
            return;
        }

        IntPtr overlayHwnd = new WindowInteropHelper(this).Handle;
        IntPtr hwndUnder = User32.WindowFromPoint(pt);

        bool onDesktopLayer =
            hwndUnder == _workerWHandle ||
            hwndUnder == overlayHwnd ||
            hwndUnder == _desktopListViewHandle;

        if (onDesktopLayer)
        {
            StopZenModeIdleTimer();
            if (_zenModeFaded)
            {
                _zenModeFaded = false;
                AnimateOverlayOpacity(1.0);
            }
            return;
        }

        // Mouse is over an app — start (or let run) the idle-to-fade timer.
        if (!_zenModeFaded && _zenModeIdleTimer == null)
        {
            double idleMs = Math.Max(100, _currentSettings.ZenModeIdleSeconds * 1000);
            double fadedOpacity = Math.Clamp(_currentSettings.ZenModeFadedOpacity, 0.0, 1.0);
            _zenModeIdleTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(idleMs)
            };
            _zenModeIdleTimer.Tick += (_, _) =>
            {
                StopZenModeIdleTimer();
                if (!_zenModeFaded)
                {
                    _zenModeFaded = true;
                    AnimateOverlayOpacity(fadedOpacity);
                }
            };
            _zenModeIdleTimer.Start();
        }
    }

    private void StopZenModeIdleTimer()
    {
        if (_zenModeIdleTimer != null)
        {
            _zenModeIdleTimer.Stop();
            _zenModeIdleTimer = null;
        }
    }

    private void AnimateOverlayOpacity(double target)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
            }
        };

        if (_desktopListViewHandle != IntPtr.Zero)
        {
            var className = new System.Text.StringBuilder(256);
            User32.GetClassName(_desktopListViewHandle, className, className.Capacity);
            if (className.ToString() == "SysListView32")
            {
                // Re-apply transparent background in case Explorer reset it during animation
                User32.SendMessage(_desktopListViewHandle, User32.LVM_SETBKCOLOR, IntPtr.Zero, (IntPtr)User32.CLR_NONE);
                User32.SendMessage(_desktopListViewHandle, User32.LVM_SETTEXTBKCOLOR, IntPtr.Zero, (IntPtr)User32.CLR_NONE);
                User32.SendMessage(_desktopListViewHandle, User32.LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)User32.LVS_EX_TRANSPARENTBKGND, (IntPtr)User32.LVS_EX_TRANSPARENTBKGND);

                anim.Completed += (s, e) => 
                {
                    RestoreDesktopListViewTransparency();

                    // Force a refresh
                    User32.InvalidateRect(_desktopListViewHandle, IntPtr.Zero, true);
                };
            }
        }

        OverlayCanvas.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void RestoreDesktopListViewTransparency()
    {
        if (_desktopListViewHandle == IntPtr.Zero)
            return;

        int exStyle = User32.GetWindowLong(_desktopListViewHandle, User32.GWL_EXSTYLE);
        if ((exStyle & User32.WS_EX_LAYERED) != 0)
        {
            User32.SetWindowLong(_desktopListViewHandle, User32.GWL_EXSTYLE, exStyle & ~User32.WS_EX_LAYERED);
        }

        User32.SendMessage(_desktopListViewHandle, User32.LVM_SETBKCOLOR, IntPtr.Zero, (IntPtr)User32.CLR_NONE);
        User32.SendMessage(_desktopListViewHandle, User32.LVM_SETTEXTBKCOLOR, IntPtr.Zero, (IntPtr)User32.CLR_NONE);
        User32.SendMessage(_desktopListViewHandle, User32.LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)User32.LVS_EX_TRANSPARENTBKGND, (IntPtr)User32.LVS_EX_TRANSPARENTBKGND);
    }

    private System.Windows.Threading.DispatcherTimer? _quickHideAutoTimer;

    private void UpdateQuickHideAutoState(POINT pt)
    {
        if (!_currentSettings.EnableQuickHide) return;

        IntPtr overlayHwnd = new WindowInteropHelper(this).Handle;
        IntPtr hwndUnder = User32.WindowFromPoint(pt);

        bool onDesktopLayer =
            hwndUnder == _workerWHandle ||
            hwndUnder == overlayHwnd ||
            hwndUnder == _desktopListViewHandle;

        if (onDesktopLayer)
        {
            StopQuickHideAutoTimer();
            if (_isQuickHidden && _currentSettings.QuickHideAutoShow)
            {
                // Auto-show
                ToggleQuickHide();
            }
        }
        else
        {
            // Mouse is over an app — check if we should auto-hide
            if (!_isQuickHidden && _currentSettings.QuickHideAutoHide && _quickHideAutoTimer == null)
            {
                _quickHideAutoTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3) // 3 seconds idle before auto-hide
                };
                _quickHideAutoTimer.Tick += (_, _) =>
                {
                    StopQuickHideAutoTimer();
                    if (!_isQuickHidden)
                    {
                        ToggleQuickHide();
                    }
                };
                _quickHideAutoTimer.Start();
            }
        }
    }

    private void StopQuickHideAutoTimer()
    {
        if (_quickHideAutoTimer != null)
        {
            _quickHideAutoTimer.Stop();
            _quickHideAutoTimer = null;
        }
    }

    private bool _isQuickHidden = false;
    private Dictionary<int, POINT> _hiddenFreeIconPositions = new();

    private void HideFreeIcons()
    {
        _hiddenFreeIconPositions.Clear();

        var containedIconIndices = new HashSet<int>(
            OverlayCanvas.Children
                .OfType<Controls.SpaceControl>()
                .SelectMany(cc => cc.AllManagedIconIndices));

        int total = ListViewManager.GetItemCount(_desktopListViewHandle);
        for (int i = 0; i < total; i++)
        {
            if (containedIconIndices.Contains(i)) continue;
            var pos = ListViewManager.GetItemPosition(_desktopListViewHandle, i);
            if (pos.HasValue)
            {
                _hiddenFreeIconPositions[i] = pos.Value;
                ListViewManager.SetItemPosition(_desktopListViewHandle, i, -10000, -10000);
            }
        }
    }

    private void RestoreFreeIcons()
    {
        foreach (var (idx, pos) in _hiddenFreeIconPositions)
            ListViewManager.SetItemPosition(_desktopListViewHandle, idx, pos.X, pos.Y);
        _hiddenFreeIconPositions.Clear();
    }

    private void ToggleQuickHide()
    {
        _isQuickHidden = !_isQuickHidden;
        var settings = _currentSettings;
        
        _logger?.LogInformation("ToggleQuickHide: New state = {State}", _isQuickHidden);

        bool hideIcons = settings.QuickHideScope == QuickHideScope.IconsAndSpaces || settings.QuickHideScope == QuickHideScope.OnlyIcons;
        bool hideSpaces = settings.QuickHideScope == QuickHideScope.IconsAndSpaces || settings.QuickHideScope == QuickHideScope.OnlySpaces;

        // 1. Toggle Overlay Canvas (Spaces & Portals)
        if (hideSpaces)
        {
            OverlayCanvas.Visibility = _isQuickHidden ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            OverlayCanvas.Visibility = Visibility.Visible;
        }

        // 2. Toggle Native Desktop Icons
        if (_desktopListViewHandle != IntPtr.Zero)
        {
            if (hideIcons)
            {
                _logger?.LogInformation("Toggling icons to: {Visibility}", _isQuickHidden ? "HIDDEN" : "VISIBLE");

                bool onlyFreeIcons = settings.QuickHideScope == QuickHideScope.OnlyIcons;

                if (onlyFreeIcons)
                {
                    // Only hide/show icons that are not owned by any space.
                    if (_isQuickHidden)
                        HideFreeIcons();
                    else
                        RestoreFreeIcons();
                }
                else
                {
                    // Hide/show the entire ListView (IconsAndSpaces scope hides spaces too,
                    // so no icons remain visible anyway).
                    User32.ShowWindow(_desktopListViewHandle, _isQuickHidden ? User32.SW_HIDE : User32.SW_SHOW);

                    if (!_isQuickHidden)
                    {
                        User32.SendMessage(_desktopListViewHandle, User32.LVM_SETBKCOLOR, IntPtr.Zero, (IntPtr)User32.CLR_NONE);
                        User32.SendMessage(_desktopListViewHandle, User32.LVM_SETTEXTBKCOLOR, IntPtr.Zero, (IntPtr)User32.CLR_NONE);
                        User32.SendMessage(_desktopListViewHandle, User32.LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)User32.LVS_EX_TRANSPARENTBKGND, (IntPtr)User32.LVS_EX_TRANSPARENTBKGND);
                    }
                }

                User32.InvalidateRect(_desktopListViewHandle, IntPtr.Zero, true);
            }
            else
            {
                User32.ShowWindow(_desktopListViewHandle, User32.SW_SHOW);
            }
        }
    }
}
