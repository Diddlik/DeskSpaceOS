using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskSpaceOS.Service;

/// <summary>
/// Extracts shell icons for files/folders using SHGetFileInfo.
/// Caches generic files by extension, folders as one folder icon, and shortcuts by full path.
/// </summary>
internal static class ShellIconExtractor
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    private static readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the shell icon for a file or directory as a WPF ImageSource.
    /// For normal files, caches by extension. Shortcuts are cached by full path because
    /// each shortcut can point to a different target icon.
    /// </summary>
    public static ImageSource? GetIcon(string fullPath, bool isDirectory)
    {
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        bool isShortcut = !isDirectory && (extension == ".lnk" || extension == ".url");
        string cacheKey = isDirectory ? "<folder>" : isShortcut ? fullPath : extension;
        if (string.IsNullOrEmpty(cacheKey)) cacheKey = "<noext>";

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var shfi = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_LARGEICON;
            uint attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            if (!isShortcut)
                flags |= SHGFI_USEFILEATTRIBUTES;

            IntPtr result = SHGetFileInfo(fullPath, attrs, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
            if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
                return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            DestroyIcon(shfi.hIcon);

            _cache[cacheKey] = source;
            return source;
        }
        catch
        {
            return null;
        }
    }
}
