using System;
using System.Runtime.InteropServices;
using DeskSpaceOS.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskSpaceOS_SettingsApp;

public sealed partial class FolderPortalDialogContent : UserControl
{
    public FolderPortalDialogContent()
    {
        InitializeComponent();
        EnableNavSwitch.IsOn = true;
    }

    public void LoadFrom(FolderPortal portal)
    {
        TitleBox.Text = portal.Title;
        PathBox.Text = portal.DirectoryPath;
        ViewModeCombo.SelectedIndex = portal.ViewMode == PortalViewMode.Details ? 1 : 0;
        SortCombo.SelectedIndex = portal.SortColumn switch
        {
            PortalSortColumn.DateModified => 1,
            PortalSortColumn.Size => 2,
            _ => 0
        };
        SortDirCombo.SelectedIndex = portal.SortAscending ? 0 : 1;
        EnableNavSwitch.IsOn = portal.EnableNavigation;
        ShowDateCheck.IsChecked = portal.ShowDateColumn;
        ShowSizeCheck.IsChecked = portal.ShowSizeColumn;
    }

    public FolderPortalDialogData GetData()
    {
        return new FolderPortalDialogData(
            TitleBox.Text.Trim(),
            PathBox.Text.Trim(),
            ViewModeCombo.SelectedIndex == 1 ? PortalViewMode.Details : PortalViewMode.Icons,
            SortCombo.SelectedIndex switch
            {
                1 => PortalSortColumn.DateModified,
                2 => PortalSortColumn.Size,
                _ => PortalSortColumn.Name
            },
            SortDirCombo.SelectedIndex == 0,
            ShowDateCheck.IsChecked == true,
            ShowSizeCheck.IsChecked == true,
            EnableNavSwitch.IsOn);
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var folderPath = PickFolderPath();
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        PathBox.Text = folderPath;

        if (string.IsNullOrWhiteSpace(TitleBox.Text))
            TitleBox.Text = GetFolderDisplayName(folderPath);
    }

    private string? PickFolderPath()
    {
        var dialog = (IFileDialog)new FileOpenDialog();
        dialog.GetOptions(out uint options);
        dialog.SetOptions((FileOpenOptions)options | FileOpenOptions.PickFolders | FileOpenOptions.ForceFileSystem |
            FileOpenOptions.PathMustExist | FileOpenOptions.NoChangeDir);
        dialog.SetTitle(Loc.Get("Portals_SelectFolder"));

        var hwnd = IntPtr.Zero;
        var window = (Application.Current as App)?.GetWindow();
        if (window != null)
            hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        var result = dialog.Show(hwnd);
        if ((uint)result == DialogCanceled)
            return null;

        Marshal.ThrowExceptionForHR(result);

        dialog.GetResult(out var item);
        item.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var pathPointer);

        try
        {
            return Marshal.PtrToStringUni(pathPointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    private static string GetFolderDisplayName(string folderPath)
    {
        var trimmedPath = folderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var folderName = System.IO.Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(folderName) ? folderPath : folderName;
    }

    private const uint DialogCanceled = 0x800704C7;

    [Flags]
    private enum FileOpenOptions : uint
    {
        PickFolders = 0x00000020,
        ForceFileSystem = 0x00000040,
        NoChangeDir = 0x00000008,
        PathMustExist = 0x00000800
    }

    private enum ShellItemDisplayName : uint
    {
        FileSystemPath = 0x80058000
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialog
    {
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(ShellItemDisplayName sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FileOpenOptions fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, uint fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid([MarshalAs(UnmanagedType.LPStruct)] Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }
}

public sealed record FolderPortalDialogData(
    string Title,
    string DirectoryPath,
    PortalViewMode ViewMode,
    PortalSortColumn SortColumn,
    bool SortAscending,
    bool ShowDateColumn,
    bool ShowSizeColumn,
    bool EnableNavigation);
