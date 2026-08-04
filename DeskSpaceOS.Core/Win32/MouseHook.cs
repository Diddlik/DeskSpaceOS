using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeskSpaceOS.Core.Win32;

public static class MouseHook
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_MOUSEWHEEL = 0x020A;

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static IntPtr _hookID = IntPtr.Zero;
    private static LowLevelMouseProc? _proc;
    private static Thread? _hookThread;
    private static uint _hookThreadId;

    public static event EventHandler<POINT>? OnLeftMouseDown;
    public static event EventHandler<POINT>? OnLeftMouseUp;
    public static event Func<POINT, bool>? OnRightMouseDown;
    public static event EventHandler<POINT>? OnMouseMove;
    public static event EventHandler<POINT>? OnLeftMouseDoubleClick;
    public static event EventHandler<MouseWheelEventData>? OnMouseWheel;

    private static uint _lastClickTime = 0;
    private static POINT _lastClickPoint;
    private static bool _suppressNextRightMouseUp;

    public static void Start()
    {
        if (_hookThread != null) return;

        var ready = new ManualResetEventSlim(false);

        // The low-level mouse hook is installed on its own dedicated thread with a
        // minimal message loop. WH_MOUSE_LL callbacks run on the installing thread
        // and sit in the system-wide mouse input path: Windows blocks every mouse
        // event until the callback returns. Keeping this off the WPF UI thread means
        // the callback is never queued behind rendering/animation work (e.g. Zen-mode
        // opacity fades), which otherwise causes global cursor lag in fullscreen apps.
        _hookThread = new Thread(() =>
        {
            _hookThreadId = GetCurrentThreadId();
            _proc = HookCallback;
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                _hookID = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            }

            ready.Set();

            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        })
        {
            IsBackground = true,
            Name = "DeskSpaceOS.MouseHook",
            Priority = ThreadPriority.Highest,
        };
        _hookThread.Start();
        ready.Wait(2000);
    }

    public static void Stop()
    {
        Thread? thread = _hookThread;
        if (thread == null) return;

        if (_hookThreadId != 0)
        {
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        thread.Join(2000);
        _hookThread = null;
        _hookThreadId = 0;

        // Fallback in case the thread never reached its message loop.
        if (_hookID != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            POINT pt = hookStruct.pt;

            if (wParam == (IntPtr)WM_LBUTTONDOWN)
            {
                uint currentTime = hookStruct.time;
                int doubleClickTime = (int)GetDoubleClickTime();
                
                if (currentTime - _lastClickTime < doubleClickTime && 
                    Math.Abs(pt.X - _lastClickPoint.X) < 5 && 
                    Math.Abs(pt.Y - _lastClickPoint.Y) < 5)
                {
                    OnLeftMouseDoubleClick?.Invoke(null, pt);
                    _lastClickTime = 0; // Reset to prevent triple click being double click
                }
                else
                {
                    OnLeftMouseDown?.Invoke(null, pt);
                    _lastClickTime = currentTime;
                    _lastClickPoint = pt;
                }
            }
            else if (wParam == (IntPtr)WM_LBUTTONUP)
            {
                OnLeftMouseUp?.Invoke(null, pt);
            }
            else if (wParam == (IntPtr)WM_RBUTTONDOWN)
            {
                if (RaiseRightMouseDown(pt))
                {
                    _suppressNextRightMouseUp = true;
                    return (IntPtr)1;
                }
            }
            else if (wParam == (IntPtr)WM_RBUTTONUP)
            {
                if (_suppressNextRightMouseUp)
                {
                    _suppressNextRightMouseUp = false;
                    return (IntPtr)1;
                }
            }
            else if (wParam == (IntPtr)WM_MOUSEMOVE)
            {
                OnMouseMove?.Invoke(null, pt);
            }
            else if (wParam == (IntPtr)WM_MOUSEWHEEL)
            {
                // High word of mouseData contains the wheel delta
                int delta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
                OnMouseWheel?.Invoke(null, new MouseWheelEventData { Point = pt, Delta = delta });
            }
        }

        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private static bool RaiseRightMouseDown(POINT pt)
    {
        var handlers = OnRightMouseDown;
        if (handlers == null) return false;

        bool handled = false;
        foreach (Func<POINT, bool> handler in handlers.GetInvocationList())
        {
            handled |= handler(pt);
        }

        return handled;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}

public class MouseWheelEventData
{
    public POINT Point { get; set; }
    public int Delta { get; set; }
}
