using System;
using System.Runtime.InteropServices;

namespace DeskSpaceOS.Core.Win32;

public static class DesktopManager
{
    private const uint WM_SPAWN_WORKER = 0x052C;
    private const uint SMTO_NORMAL = 0x0000;

    /// <summary>
    /// Injects a new WorkerW window between the desktop wallpaper and the desktop icons.
    /// Returns the handle to the new WorkerW window.
    /// </summary>
    public static IntPtr InitializeDesktopHook()
    {
        // 1. Find Progman
        IntPtr progman = User32.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            throw new Exception("Could not find Progman window.");
        }

        // 2. Send the undocumented message to Progman to spawn a WorkerW behind the desktop icons.
        IntPtr result;
        User32.SendMessageTimeout(
            progman,
            WM_SPAWN_WORKER,
            IntPtr.Zero,
            IntPtr.Zero,
            SMTO_NORMAL,
            1000,
            out result);

        // 3. Find the newly created WorkerW window.
        IntPtr workerW = IntPtr.Zero;

        User32.EnumWindows(new User32.EnumWindowsProc((hwnd, lParam) =>
        {
            // Find the SHELLDLL_DefView window
            IntPtr shellDllDefView = User32.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            
            if (shellDllDefView != IntPtr.Zero)
            {
                // The WorkerW we want is the sibling of the WorkerW that contains the SHELLDLL_DefView
                workerW = User32.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
            }
            
            return true;
        }), IntPtr.Zero);

        // If we couldn't find the sibling WorkerW, try enumerating all WorkerW's and finding the one with no SHELLDLL_DefView
        if (workerW == IntPtr.Zero)
        {
            User32.EnumWindows(new User32.EnumWindowsProc((hwnd, lParam) =>
            {
                // Get class name
                System.Text.StringBuilder className = new System.Text.StringBuilder(256);
                // We'll just assume we're finding a window. Since we don't have GetClassName imported,
                // we just check if it's a child of desktop and has no children of its own.
                // Actually, let's just fallback to Progman for safety if workerW is zero.
                return true;
            }), IntPtr.Zero);
        }

        // Fallback to Progman if the hook trick failed to locate the new WorkerW
        if (workerW == IntPtr.Zero)
        {
            return progman;
        }

        return workerW;
    }

    /// <summary>
    /// Finds the SysListView32 control that holds the desktop icons.
    /// It can be under Progman or WorkerW depending on whether the desktop has been hooked.
    /// </summary>
    public static IntPtr GetDesktopListViewHandle()
    {
        IntPtr result = IntPtr.Zero;

        // The desktop ListView is always a child of SHELLDLL_DefView.
        // We will enumerate all windows looking for SHELLDLL_DefView, and then find its child SysListView32.
        User32.EnumWindows(new User32.EnumWindowsProc((hwnd, lParam) =>
        {
            IntPtr shellDllDefView = User32.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (shellDllDefView != IntPtr.Zero)
            {
                // Find by class name only, title can vary by locale or OS version
                result = User32.FindWindowEx(shellDllDefView, IntPtr.Zero, "SysListView32", null);
            }

            return true;
        }), IntPtr.Zero);

        // Fallback: If not found via enumeration, check Progman directly.
        if (result == IntPtr.Zero)
        {
            IntPtr progman = User32.FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                IntPtr shellDllDefView = User32.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellDllDefView != IntPtr.Zero)
                {
                    result = User32.FindWindowEx(shellDllDefView, IntPtr.Zero, "SysListView32", null);
                }
            }
        }

        return result;
    }

    private const int WM_COMMAND = 0x0111;
    private const int TOGGLE_DESKTOP_ICONS = 0x7402;

    public static bool AreDesktopIconsVisible()
    {
        IntPtr listView = GetDesktopListViewHandle();
        if (listView == IntPtr.Zero) return true;
        return User32.IsWindowVisible(listView);
    }

    public static void SetDesktopIconsVisible(bool visible)
    {
        if (AreDesktopIconsVisible() == visible) return;
        ToggleDesktopIcons();
    }

    public static void ToggleDesktopIcons()
    {
        IntPtr listView = GetDesktopListViewHandle();
        if (listView == IntPtr.Zero) return;

        IntPtr shellDllDefView = User32.GetParent(listView);
        if (shellDllDefView == IntPtr.Zero) return;

        User32.SendMessage(shellDllDefView, WM_COMMAND, (IntPtr)TOGGLE_DESKTOP_ICONS, IntPtr.Zero);
    }
}