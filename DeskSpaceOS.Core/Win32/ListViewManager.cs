using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DeskSpaceOS.Core.Win32;

public static class ListViewManager
{
    private const uint LVM_FIRST = 0x1000;
    private const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
    private const uint LVM_GETITEMPOSITION = LVM_FIRST + 16;
    private const uint LVM_SETITEMPOSITION = LVM_FIRST + 15;
    private const uint LVM_GETITEMTEXTW = LVM_FIRST + 115;
    private const uint LVM_GETSELECTEDCOUNT = LVM_FIRST + 50;
    private const uint LVM_GETNEXTITEM = LVM_FIRST + 12;
    private const int LVNI_SELECTED = 0x0002;

    private const int LVS_AUTOARRANGE = 0x0100;

    private const int LVIF_TEXT = 0x0001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LVITEM
    {
        public int mask;
        public int iItem;
        public int iSubItem;
        public int state;
        public int stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out uint lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out uint lpNumberOfBytesWritten);

    public static bool IsAutoArrangeEnabled(IntPtr listViewHandle)
    {
        int style = User32.GetWindowLong(listViewHandle, User32.GWL_STYLE);
        return (style & LVS_AUTOARRANGE) != 0;
    }

    public static void SetAutoArrange(IntPtr listViewHandle, bool enabled)
    {
        int style = User32.GetWindowLong(listViewHandle, User32.GWL_STYLE);
        if (enabled)
            style |= LVS_AUTOARRANGE;
        else
            style &= ~LVS_AUTOARRANGE;
        User32.SetWindowLong(listViewHandle, User32.GWL_STYLE, style);
    }

    public static int GetItemCount(IntPtr listViewHandle)
    {
        return (int)User32.SendMessage(listViewHandle, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
    }

    public static POINT? GetItemPosition(IntPtr listViewHandle, int index)
    {
        uint processId;
        User32.GetWindowThreadProcessId(listViewHandle, out processId);

        IntPtr processHandle = Kernel32.OpenProcess(
            Kernel32.PROCESS_VM_OPERATION | Kernel32.PROCESS_VM_READ | Kernel32.PROCESS_VM_WRITE,
            false,
            processId);

        if (processHandle == IntPtr.Zero)
            return null;

        IntPtr remoteBuffer = Kernel32.VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            (uint)Marshal.SizeOf(typeof(POINT)),
            Kernel32.MEM_COMMIT | Kernel32.MEM_RESERVE,
            Kernel32.PAGE_READWRITE);

        if (remoteBuffer == IntPtr.Zero)
        {
            Kernel32.CloseHandle(processHandle);
            return null;
        }

        try
        {
            User32.SendMessage(listViewHandle, LVM_GETITEMPOSITION, (IntPtr)index, remoteBuffer);

            POINT point;
            uint bytesRead;
            if (Kernel32.ReadProcessMemory(processHandle, remoteBuffer, out point, (uint)Marshal.SizeOf(typeof(POINT)), out bytesRead))
            {
                return point;
            }
            return null;
        }
        finally
        {
            Kernel32.VirtualFreeEx(processHandle, remoteBuffer, 0, Kernel32.MEM_RELEASE);
            Kernel32.CloseHandle(processHandle);
        }
    }

    public static void SetItemPosition(IntPtr listViewHandle, int index, int x, int y)
    {
        // For LVM_SETITEMPOSITION, wParam is index, lParam is MAKELPARAM(x, y)
        IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));
        User32.SendMessage(listViewHandle, LVM_SETITEMPOSITION, (IntPtr)index, lParam);
    }

    public static List<POINT> GetAllItemPositions(IntPtr listViewHandle)
    {
        var positions = new List<POINT>();
        int count = GetItemCount(listViewHandle);
        
        for (int i = 0; i < count; i++)
        {
            var pos = GetItemPosition(listViewHandle, i);
            if (pos.HasValue)
            {
                positions.Add(pos.Value);
            }
        }
        
        return positions;
    }
    
    public static string? GetItemText(IntPtr listViewHandle, int index)
    {
        uint processId;
        User32.GetWindowThreadProcessId(listViewHandle, out processId);

        IntPtr processHandle = Kernel32.OpenProcess(
            Kernel32.PROCESS_VM_OPERATION | Kernel32.PROCESS_VM_READ | Kernel32.PROCESS_VM_WRITE,
            false, processId);
        if (processHandle == IntPtr.Zero) return null;

        const int bufferSize = 512;
        IntPtr remoteBuffer = IntPtr.Zero;
        IntPtr remoteText = IntPtr.Zero;

        try
        {
            // Allocate memory in the remote process for the text buffer
            remoteText = Kernel32.VirtualAllocEx(processHandle, IntPtr.Zero, (uint)bufferSize,
                Kernel32.MEM_COMMIT | Kernel32.MEM_RESERVE, Kernel32.PAGE_READWRITE);
            if (remoteText == IntPtr.Zero) return null;

            // Allocate memory for the LVITEM struct
            int lvitemSize = Marshal.SizeOf<LVITEM>();
            remoteBuffer = Kernel32.VirtualAllocEx(processHandle, IntPtr.Zero, (uint)lvitemSize,
                Kernel32.MEM_COMMIT | Kernel32.MEM_RESERVE, Kernel32.PAGE_READWRITE);
            if (remoteBuffer == IntPtr.Zero) return null;

            // Set up the LVITEM struct
            var item = new LVITEM
            {
                mask = LVIF_TEXT,
                iItem = index,
                iSubItem = 0,
                pszText = remoteText,
                cchTextMax = bufferSize / 2 // Unicode chars
            };

            byte[] itemBytes = new byte[lvitemSize];
            IntPtr itemPtr = Marshal.AllocHGlobal(lvitemSize);
            try
            {
                Marshal.StructureToPtr(item, itemPtr, false);
                Marshal.Copy(itemPtr, itemBytes, 0, lvitemSize);
            }
            finally
            {
                Marshal.FreeHGlobal(itemPtr);
            }

            // Write LVITEM to remote process
            uint written;
            WriteProcessMemory(processHandle, remoteBuffer, itemBytes, (uint)lvitemSize, out written);

            // Send the message
            User32.SendMessage(listViewHandle, LVM_GETITEMTEXTW, (IntPtr)index, remoteBuffer);

            // Read the text back
            byte[] textBytes = new byte[bufferSize];
            uint bytesRead;
            ReadProcessMemory(processHandle, remoteText, textBytes, (uint)bufferSize, out bytesRead);

            return System.Text.Encoding.Unicode.GetString(textBytes).TrimEnd('\0');
        }
        finally
        {
            if (remoteText != IntPtr.Zero)
                Kernel32.VirtualFreeEx(processHandle, remoteText, 0, Kernel32.MEM_RELEASE);
            if (remoteBuffer != IntPtr.Zero)
                Kernel32.VirtualFreeEx(processHandle, remoteBuffer, 0, Kernel32.MEM_RELEASE);
            Kernel32.CloseHandle(processHandle);
        }
    }

    public static int FindItemByName(IntPtr listViewHandle, string name)
    {
        int count = GetItemCount(listViewHandle);
        for (int i = 0; i < count; i++)
        {
            string? text = GetItemText(listViewHandle, i);
            if (text == name)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Returns the number of currently selected items in the ListView.
    /// </summary>
    public static int GetSelectedCount(IntPtr listViewHandle)
    {
        return (int)User32.SendMessage(listViewHandle, LVM_GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Returns indices of all currently selected items in the ListView.
    /// </summary>
    public static List<int> GetSelectedIndices(IntPtr listViewHandle)
    {
        var indices = new List<int>();
        int index = -1;
        while (true)
        {
            index = (int)User32.SendMessage(listViewHandle, LVM_GETNEXTITEM, (IntPtr)index, (IntPtr)LVNI_SELECTED);
            if (index == -1) break;
            indices.Add(index);
        }
        return indices;
    }

    public static bool IsPointOnIcon(IntPtr listViewHandle, int screenX, int screenY)
    {
        return FindIconAtPoint(listViewHandle, screenX, screenY) >= 0;
    }

    /// <summary>
    /// Returns the ListView index of the icon at the given screen point, or -1 if none.
    /// </summary>
    public static int FindIconAtPoint(IntPtr listViewHandle, int screenX, int screenY)
    {
        int count = GetItemCount(listViewHandle);
        const int iconWidth = 80;
        const int iconHeight = 100;

        for (int i = 0; i < count; i++)
        {
            var pos = GetItemPosition(listViewHandle, i);
            if (pos.HasValue)
            {
                if (screenX >= pos.Value.X && screenX <= pos.Value.X + iconWidth &&
                    screenY >= pos.Value.Y && screenY <= pos.Value.Y + iconHeight)
                {
                    return i;
                }
            }
        }
        return -1;
    }
}