using System;
using System.Diagnostics;

namespace MicaSetup.Shell.Dialogs;

public static class KnownFolderHelper
{
    public static IKnownFolder FromCanonicalName(string canonicalName)
    {
        var knownFolderManager = (IKnownFolderManager)new KnownFolderManagerClass();

        knownFolderManager.GetFolderByName(canonicalName, out var knownFolderNative);
        var kf = KnownFolderHelper.GetKnownFolder(knownFolderNative);
        return kf ?? throw new ArgumentException(LocalizedMessages.ShellInvalidCanonicalName, "canonicalName");
    }

    public static IKnownFolder FromKnownFolderId(Guid knownFolderId)
    {
        var knownFolderManager = new KnownFolderManagerClass();

        var hr = knownFolderManager.GetFolder(knownFolderId, out var knownFolderNative);
        if (hr != HResult.Ok) { throw new ShellException(hr); }

        var kf = GetKnownFolder(knownFolderNative);
        return kf ?? throw new ArgumentException(LocalizedMessages.KnownFolderInvalidGuid, "knownFolderId");
    }

    public static IKnownFolder FromParsingName(string parsingName)
    {
        if (parsingName == null)
        {
            throw new ArgumentNullException("parsingName");
        }

        nint pidl = 0;
        nint pidl2 = 0;

        try
        {
            pidl = ShellHelper.PidlFromParsingName(parsingName);

            if (pidl == 0)
            {
                throw new ArgumentException(LocalizedMessages.KnownFolderParsingName, "parsingName");
            }

            var knownFolderNative = KnownFolderHelper.FromPIDL(pidl);
            if (knownFolderNative != null)
            {
                var kf = KnownFolderHelper.GetKnownFolder(knownFolderNative);
                return kf ?? throw new ArgumentException(LocalizedMessages.KnownFolderParsingName, "parsingName");
            }

            pidl2 = ShellHelper.PidlFromParsingName(parsingName.PadRight(1, '\0'));

            if (pidl2 == 0)
            {
                throw new ArgumentException(LocalizedMessages.KnownFolderParsingName, "parsingName");
            }

            var kf2 = KnownFolderHelper.GetKnownFolder(KnownFolderHelper.FromPIDL(pidl));
            return kf2 ?? throw new ArgumentException(LocalizedMessages.KnownFolderParsingName, "parsingName");
        }
        finally
        {
            Shell32.ILFree(pidl);
            Shell32.ILFree(pidl2);
        }
    }

    public static IKnownFolder FromPath(string path) => KnownFolderHelper.FromParsingName(path);

    internal static IKnownFolder FromKnownFolderIdInternal(Guid knownFolderId)
    {
        var knownFolderManager = (IKnownFolderManager)new KnownFolderManagerClass();

        var hr = knownFolderManager.GetFolder(knownFolderId, out var knownFolderNative);

        return (hr == HResult.Ok) ? GetKnownFolder(knownFolderNative) : null!;
    }

    internal static IKnownFolderNative FromPIDL(nint pidl)
    {
        var knownFolderManager = new KnownFolderManagerClass();

        var hr = knownFolderManager.FindFolderFromIDList(pidl, out var knownFolder);

        return (hr == HResult.Ok) ? knownFolder : null!;
    }

    private static IKnownFolder GetKnownFolder(IKnownFolderNative knownFolderNative)
    {
        Debug.Assert(knownFolderNative != null, "Native IKnownFolder should not be null.");

        var guid = new Guid(ShellIIDGuid.IShellItem2);
        HResult hr = knownFolderNative!.GetShellItem(0, ref guid, out var shellItem);

        if (!CoreErrorHelper.Succeeded(hr)) { return null!; }

        var isFileSystem = false;

        if (shellItem != null)
        {
            shellItem.GetAttributes(ShellFileGetAttributesOptions.FileSystem, out var sfgao);

            isFileSystem = (sfgao & ShellFileGetAttributesOptions.FileSystem) != 0;
        }

        if (isFileSystem)
        {
            var kf = new FileSystemKnownFolder(knownFolderNative);
            return kf;
        }

        var knownFsFolder = new NonFileSystemKnownFolder(knownFolderNative);
        return knownFsFolder;
    }
}
