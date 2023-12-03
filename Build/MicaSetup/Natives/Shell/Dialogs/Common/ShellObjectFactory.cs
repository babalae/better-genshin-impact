using MicaSetup.Helper;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MicaSetup.Shell.Dialogs;

internal static class ShellObjectFactory
{
    internal static ShellObject Create(IShellItem nativeShellItem)
    {
        Debug.Assert(nativeShellItem != null, "nativeShellItem should not be null");

        if (!OsVersionHelper.IsWindowsVista_OrGreater)
        {
            throw new PlatformNotSupportedException(LocalizedMessages.ShellObjectFactoryPlatformNotSupported);
        }

        var nativeShellItem2 = nativeShellItem as IShellItem2;

        var itemType = ShellHelper.GetItemType(nativeShellItem2!);

        if (!string.IsNullOrEmpty(itemType)) { itemType = itemType.ToLowerInvariant(); }

        nativeShellItem2!.GetAttributes(ShellFileGetAttributesOptions.FileSystem | ShellFileGetAttributesOptions.Folder, out var sfgao);

        var isFileSystem = (sfgao & ShellFileGetAttributesOptions.FileSystem) != 0;

        var isFolder = (sfgao & ShellFileGetAttributesOptions.Folder) != 0;
        if (StringComparer.OrdinalIgnoreCase.Equals(itemType, ".lnk"))
        {
            return new ShellLink(nativeShellItem2);
        }
        else if (isFolder)
        {
            ShellLibrary shellLibrary;
            if (itemType == ".library-ms" && (shellLibrary = ShellLibrary.FromShellItem(nativeShellItem2, true)) != null!)
            {
                return shellLibrary;
            }
            else if (itemType == ".searchconnector-ms")
            {
                return new ShellSearchConnector(nativeShellItem2);
            }
            else if (itemType == ".search-ms")
            {
                return new ShellSavedSearchCollection(nativeShellItem2);
            }

            if (isFileSystem)
            {
                if (!IsVirtualKnownFolder(nativeShellItem2))
                {
                    var kf = new FileSystemKnownFolder(nativeShellItem2);
                    return kf;
                }

                return new ShellFileSystemFolder(nativeShellItem2);
            }

            if (IsVirtualKnownFolder(nativeShellItem2))
            {
                var kf = new NonFileSystemKnownFolder(nativeShellItem2);
                return kf;
            }

            return new ShellNonFileSystemFolder(nativeShellItem2);
        }

        if (isFileSystem) { return new ShellFile(nativeShellItem2); }

        return new ShellNonFileSystemItem(nativeShellItem2);
    }

    internal static ShellObject Create(string parsingName)
    {
        if (string.IsNullOrEmpty(parsingName))
        {
            throw new ArgumentNullException("parsingName");
        }

        var guid = new Guid(ShellIIDGuid.IShellItem2);
        var retCode = Shell32.SHCreateItemFromParsingName(parsingName, 0, ref guid, out
        IShellItem2 nativeShellItem);

        if (!CoreErrorHelper.Succeeded(retCode))
        {
            throw new ShellException(LocalizedMessages.ShellObjectFactoryUnableToCreateItem, Marshal.GetExceptionForHR(retCode));
        }
        return ShellObjectFactory.Create(nativeShellItem);
    }

    internal static ShellObject Create(nint idListPtr)
    {
        OsVersionHelper.ThrowIfNotVista();

        var guid = new Guid(ShellIIDGuid.IShellItem2);

        var retCode = Shell32.SHCreateItemFromIDList(idListPtr, ref guid, out var nativeShellItem);

        if (!CoreErrorHelper.Succeeded(retCode)) { return null!; }
        return ShellObjectFactory.Create(nativeShellItem);
    }

    internal static ShellObject Create(nint idListPtr, ShellContainer parent)
    {
        var retCode = Shell32.SHCreateShellItem(
            0,
            parent.NativeShellFolder,
            idListPtr, out var nativeShellItem);

        if (!CoreErrorHelper.Succeeded(retCode)) { return null!; }

        return ShellObjectFactory.Create(nativeShellItem);
    }

    private static bool IsVirtualKnownFolder(IShellItem2 nativeShellItem2)
    {
        nint pidl = 0;
        try
        {
            IKnownFolderNative nativeFolder = null!;
            var definition = new KnownFoldersSafeNativeMethods.NativeFolderDefinition();

            var padlock = new object();
            lock (padlock)
            {
                var unknown = Marshal.GetIUnknownForObject(nativeShellItem2);

                ThreadPool.QueueUserWorkItem(obj =>
                {
                    lock (padlock)
                    {
                        pidl = ShellHelper.PidlFromUnknown(unknown);

                        new KnownFolderManagerClass().FindFolderFromIDList(pidl, out nativeFolder);

                        nativeFolder?.GetFolderDefinition(out definition);

                        Monitor.Pulse(padlock);
                    }
                });

                Monitor.Wait(padlock);
            }

            return nativeFolder != null && definition.category == FolderCategory.Virtual;
        }
        finally
        {
            Shell32.ILFree(pidl);
        }
    }
}
