using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DeskSpaceOS.Core.Win32;

namespace DeskSpaceOS.Service;

public partial class SelectionWindow : Window
{
    private readonly IntPtr _workerWHandle;

    public SelectionWindow(IntPtr workerWHandle)
    {
        InitializeComponent();
        _workerWHandle = workerWHandle;

        // Cover the entire virtual screen
        this.Left = SystemParameters.VirtualScreenLeft;
        this.Top = SystemParameters.VirtualScreenTop;
        this.Width = SystemParameters.VirtualScreenWidth;
        this.Height = SystemParameters.VirtualScreenHeight;

        this.Loaded += SelectionWindow_Loaded;
    }

    private void SelectionWindow_Loaded(object sender, RoutedEventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        // Make this window click-through
        int exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE, exStyle | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOOLWINDOW);

        // Pin to desktop layer — just above WorkerW, behind applications
        if (_workerWHandle != IntPtr.Zero)
        {
            User32.SetWindowPos(hwnd, _workerWHandle,
                0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
        }
    }

    public void ShowSelection(double x, double y, double width, double height)
    {
        Canvas.SetLeft(SelectionRect, x - this.Left);
        Canvas.SetTop(SelectionRect, y - this.Top);
        SelectionRect.Width = width;
        SelectionRect.Height = height;
        SelectionRect.Visibility = Visibility.Visible;
    }

    public void HideSelection()
    {
        SelectionRect.Visibility = Visibility.Collapsed;
    }
}
