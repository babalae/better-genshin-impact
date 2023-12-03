using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable IDE0059

internal static class ShellHelper
{
    internal static PropertyKey ItemTypePropertyKey = new PropertyKey(new Guid("28636AA6-953D-11D2-B5D6-00C04FD918D0"), 11);

    internal static string GetAbsolutePath(string path)
    {
        if (Uri.IsWellFormedUriString(path, UriKind.Absolute))
        {
            return path;
        }
        return Path.GetFullPath((path));
    }

    internal static string GetItemType(IShellItem2 shellItem)
    {
        if (shellItem != null)
        {
            var hr = shellItem.GetString(ref ItemTypePropertyKey, out var itemType);
            if (hr == HResult.Ok) { return itemType; }
        }

        return null!;
    }

    internal static string GetParsingName(IShellItem shellItem)
    {
        if (shellItem == null) { return null!; }

        string path = null!;

        var hr = shellItem.GetDisplayName(ShellItemDesignNameOptions.DesktopAbsoluteParsing, out nint pszPath);

        if (hr != HResult.Ok && hr != HResult.InvalidArguments)
        {
            throw new ShellException(LocalizedMessages.ShellHelperGetParsingNameFailed, hr);
        }

        if (pszPath != 0)
        {
            path = Marshal.PtrToStringAuto(pszPath);
            Marshal.FreeCoTaskMem(pszPath);
            pszPath = 0;
        }

        return path;
    }

    internal static nint PidlFromParsingName(string name)
    {
        var retCode = Shell32.SHParseDisplayName(
            name, 0, out nint pidl, 0,
            out _);

        return (CoreErrorHelper.Succeeded(retCode) ? pidl : 0);
    }

    internal static nint PidlFromShellItem(IShellItem nativeShellItem)
    {
        var unknown = Marshal.GetIUnknownForObject(nativeShellItem);
        return PidlFromUnknown(unknown);
    }

    internal static nint PidlFromUnknown(nint unknown)
    {
        var retCode = Shell32.SHGetIDListFromObject(unknown, out nint pidl);
        return (CoreErrorHelper.Succeeded(retCode) ? pidl : 0);
    }
}
