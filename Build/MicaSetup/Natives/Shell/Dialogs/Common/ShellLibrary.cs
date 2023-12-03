using MicaSetup.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8601
#pragma warning disable CS8618
#pragma warning disable IDE0059

public sealed class ShellLibrary : ShellContainer, IList<ShellFileSystemFolder>
{
    internal const string FileExtension = ".library-ms";

    private static readonly Guid[] FolderTypesGuids =
    {
        new Guid(ShellKFIDGuid.GenericLibrary),
        new Guid(ShellKFIDGuid.DocumentsLibrary),
        new Guid(ShellKFIDGuid.MusicLibrary),
        new Guid(ShellKFIDGuid.PicturesLibrary),
        new Guid(ShellKFIDGuid.VideosLibrary)
    };

    private readonly IKnownFolder knownFolder;
    private INativeShellLibrary nativeShellLibrary;

    public ShellLibrary(string libraryName, bool overwrite)
        : this()
    {
        if (string.IsNullOrEmpty(libraryName))
        {
            throw new ArgumentException(LocalizedMessages.ShellLibraryEmptyName, "libraryName");
        }

        Name = libraryName;
        var guid = new Guid(ShellKFIDGuid.Libraries);

        var flags = overwrite ?
                LibrarySaveOptions.OverrideExisting :
                LibrarySaveOptions.FailIfThere;

        nativeShellLibrary = (INativeShellLibrary)new ShellLibraryCoClass();
        nativeShellLibrary.SaveInKnownFolder(ref guid, libraryName, flags, out nativeShellItem);
    }

    public ShellLibrary(string libraryName, IKnownFolder sourceKnownFolder, bool overwrite)
        : this()
    {
        if (string.IsNullOrEmpty(libraryName))
        {
            throw new ArgumentException(LocalizedMessages.ShellLibraryEmptyName, "libraryName");
        }

        knownFolder = sourceKnownFolder;

        Name = libraryName;
        var guid = knownFolder.FolderId;

        var flags = overwrite ?
                LibrarySaveOptions.OverrideExisting :
                LibrarySaveOptions.FailIfThere;

        nativeShellLibrary = (INativeShellLibrary)new ShellLibraryCoClass();
        nativeShellLibrary.SaveInKnownFolder(ref guid, libraryName, flags, out nativeShellItem);
    }

    public ShellLibrary(string libraryName, string folderPath, bool overwrite)
        : this()
    {
        if (string.IsNullOrEmpty(libraryName))
        {
            throw new ArgumentException(LocalizedMessages.ShellLibraryEmptyName, "libraryName");
        }

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException(LocalizedMessages.ShellLibraryFolderNotFound);
        }

        Name = libraryName;

        var flags = overwrite ?
                LibrarySaveOptions.OverrideExisting :
                LibrarySaveOptions.FailIfThere;

        var guid = new Guid(ShellIIDGuid.IShellItem);

        Shell32.SHCreateItemFromParsingName(folderPath, 0, ref guid, out
        IShellItem shellItemIn);

        nativeShellLibrary = (INativeShellLibrary)new ShellLibraryCoClass();
        nativeShellLibrary.Save(shellItemIn, libraryName, flags, out nativeShellItem);
    }

    private ShellLibrary() => OsVersionHelper.ThrowIfNotWin7();

    private ShellLibrary(INativeShellLibrary nativeShellLibrary)
        : this() => this.nativeShellLibrary = nativeShellLibrary;

    private ShellLibrary(IKnownFolder sourceKnownFolder, bool isReadOnly)
        : this()
    {
        Debug.Assert(sourceKnownFolder != null);

        knownFolder = sourceKnownFolder;

        nativeShellLibrary = (INativeShellLibrary)new ShellLibraryCoClass();

        var flags = isReadOnly ?
                AccessModes.Read :
                AccessModes.ReadWrite;

        base.nativeShellItem = ((ShellObject)sourceKnownFolder!).NativeShellItem2;

        var guid = sourceKnownFolder.FolderId;

        try
        {
            nativeShellLibrary.LoadLibraryFromKnownFolder(ref guid, flags);
        }
        catch (InvalidCastException)
        {
            throw new ArgumentException(LocalizedMessages.ShellLibraryInvalidLibrary, "sourceKnownFolder");
        }
        catch (NotImplementedException)
        {
            throw new ArgumentException(LocalizedMessages.ShellLibraryInvalidLibrary, "sourceKnownFolder");
        }
    }

    ~ShellLibrary()
    {
        Dispose(false);
    }

    public new static bool IsPlatformSupported =>
            OsVersionHelper.IsWindows7_OrGreater;

    public static IKnownFolder LibrariesKnownFolder
    {
        get
        {
            OsVersionHelper.ThrowIfNotWin7();
            return KnownFolderHelper.FromKnownFolderId(new Guid(ShellKFIDGuid.Libraries));
        }
    }

    public string DefaultSaveFolder
    {
        get
        {
            var guid = new Guid(ShellIIDGuid.IShellItem);

            nativeShellLibrary.GetDefaultSaveFolder(
                DefaultSaveFolderType.Detect,
                ref guid,
                out var saveFolderItem);

            return ShellHelper.GetParsingName(saveFolderItem);
        }
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException("value");
            }

            if (!Directory.Exists(value))
            {
                throw new DirectoryNotFoundException(LocalizedMessages.ShellLibraryDefaultSaveFolderNotFound);
            }

            var fullPath = new DirectoryInfo(value).FullName;

            var guid = new Guid(ShellIIDGuid.IShellItem);

            Shell32.SHCreateItemFromParsingName(fullPath, 0, ref guid, out IShellItem saveFolderItem);

            nativeShellLibrary.SetDefaultSaveFolder(
                DefaultSaveFolderType.Detect,
                saveFolderItem);

            nativeShellLibrary.Commit();
        }
    }

    public IconReference IconResourceId
    {
        get
        {
            nativeShellLibrary.GetIcon(out var iconRef);
            return new IconReference(iconRef);
        }

        set
        {
            nativeShellLibrary.SetIcon(value.ReferencePath);
            nativeShellLibrary.Commit();
        }
    }

    public bool IsPinnedToNavigationPane
    {
        get
        {
            nativeShellLibrary.GetOptions(out LibraryOptions flags);

            return (
                (flags & LibraryOptions.PinnedToNavigationPane) ==
                LibraryOptions.PinnedToNavigationPane);
        }
        set
        {
            var flags = LibraryOptions.Default;

            if (value)
            {
                flags |= LibraryOptions.PinnedToNavigationPane;
            }
            else
            {
                flags &= ~LibraryOptions.PinnedToNavigationPane;
            }

            nativeShellLibrary.SetOptions(LibraryOptions.PinnedToNavigationPane, flags);
            nativeShellLibrary.Commit();
        }
    }

    public bool IsReadOnly => false;

    public LibraryFolderType LibraryType
    {
        get
        {
            nativeShellLibrary.GetFolderType(out var folderTypeGuid);

            return GetFolderTypefromGuid(folderTypeGuid);
        }

        set
        {
            var guid = FolderTypesGuids[(int)value];
            nativeShellLibrary.SetFolderType(ref guid);
            nativeShellLibrary.Commit();
        }
    }

    public Guid LibraryTypeId
    {
        get
        {
            nativeShellLibrary.GetFolderType(out var folderTypeGuid);

            return folderTypeGuid;
        }
    }

    public override string Name
    {
        get
        {
            if (base.Name == null && NativeShellItem != null)
            {
                base.Name = System.IO.Path.GetFileNameWithoutExtension(ShellHelper.GetParsingName(NativeShellItem));
            }

            return base.Name!;
        }
    }

    public int Count => ItemsList.Count;

    internal override IShellItem NativeShellItem => NativeShellItem2;

    internal override IShellItem2 NativeShellItem2 => nativeShellItem;

    private List<ShellFileSystemFolder> ItemsList => GetFolders();

    public ShellFileSystemFolder this[int index]
    {
        get => ItemsList[index];
        set =>
            throw new NotImplementedException();
    }

    public static ShellLibrary Load(string libraryName, bool isReadOnly)
    {
        OsVersionHelper.ThrowIfNotWin7();

        var kf = KnownFolders.Libraries;
        var librariesFolderPath = (kf != null) ? kf.Path : string.Empty;

        var guid = new Guid(ShellIIDGuid.IShellItem);
        var shellItemPath = System.IO.Path.Combine(librariesFolderPath, libraryName + FileExtension);
        var hr = Shell32.SHCreateItemFromParsingName(shellItemPath, 0, ref guid, out IShellItem nativeShellItem);

        if (!CoreErrorHelper.Succeeded(hr))
            throw new ShellException(hr);

        var nativeShellLibrary = (INativeShellLibrary)new ShellLibraryCoClass();
        var flags = isReadOnly ?
                AccessModes.Read :
                AccessModes.ReadWrite;
        nativeShellLibrary.LoadLibraryFromItem(nativeShellItem, flags);

        var library = new ShellLibrary(nativeShellLibrary);
        try
        {
            library.nativeShellItem = (IShellItem2)nativeShellItem;
            library.Name = libraryName;

            return library;
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    public static ShellLibrary Load(string libraryName, string folderPath, bool isReadOnly)
    {
        OsVersionHelper.ThrowIfNotWin7();

        var shellItemPath = System.IO.Path.Combine(folderPath, libraryName + FileExtension);
        var item = ShellFile.FromFilePath(shellItemPath);

        var nativeShellItem = item.NativeShellItem;
        var nativeShellLibrary = (INativeShellLibrary)new ShellLibraryCoClass();
        var flags = isReadOnly ?
                AccessModes.Read :
                AccessModes.ReadWrite;
        nativeShellLibrary.LoadLibraryFromItem(nativeShellItem, flags);

        var library = new ShellLibrary(nativeShellLibrary);
        try
        {
            library.nativeShellItem = (IShellItem2)nativeShellItem;
            library.Name = libraryName;

            return library;
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    public static ShellLibrary Load(IKnownFolder sourceKnownFolder, bool isReadOnly)
    {
        OsVersionHelper.ThrowIfNotWin7();
        return new ShellLibrary(sourceKnownFolder, isReadOnly);
    }

    public static void ShowManageLibraryUI(string libraryName, string folderPath, nint windowHandle, string title, string instruction, bool allowAllLocations)
    {
        using ShellLibrary shellLibrary = ShellLibrary.Load(libraryName, folderPath, true);
        ShowManageLibraryUI(shellLibrary, windowHandle, title, instruction, allowAllLocations);
    }

    public static void ShowManageLibraryUI(string libraryName, nint windowHandle, string title, string instruction, bool allowAllLocations)
    {
        using ShellLibrary shellLibrary = ShellLibrary.Load(libraryName, true);
        ShowManageLibraryUI(shellLibrary, windowHandle, title, instruction, allowAllLocations);
    }

    public static void ShowManageLibraryUI(IKnownFolder sourceKnownFolder, nint windowHandle, string title, string instruction, bool allowAllLocations)
    {
        using ShellLibrary shellLibrary = Load(sourceKnownFolder, true);
        ShowManageLibraryUI(shellLibrary, windowHandle, title, instruction, allowAllLocations);
    }

    public void Add(ShellFileSystemFolder item)
    {
        if (item == null!) { throw new ArgumentNullException("item"); }

        nativeShellLibrary.AddFolder(item.NativeShellItem);
        nativeShellLibrary.Commit();
    }

    public void Add(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException(LocalizedMessages.ShellLibraryFolderNotFound);
        }

        Add(ShellFileSystemFolder.FromFolderPath(folderPath));
    }

    public void Clear()
    {
        var list = ItemsList;
        foreach (var folder in list)
        {
            nativeShellLibrary.RemoveFolder(folder.NativeShellItem);
        }

        nativeShellLibrary.Commit();
    }

    public void Close() => Dispose();

    public bool Contains(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            throw new ArgumentNullException("fullPath");
        }

        return ItemsList.Any(folder => string.Equals(fullPath, folder.Path, StringComparison.OrdinalIgnoreCase));
    }

    public bool Contains(ShellFileSystemFolder item)
    {
        if (item == null!)
        {
            throw new ArgumentNullException("item");
        }

        return ItemsList.Any(folder => string.Equals(item.Path, folder.Path, StringComparison.OrdinalIgnoreCase));
    }

    public new IEnumerator<ShellFileSystemFolder> GetEnumerator() => ItemsList.GetEnumerator();

    public int IndexOf(ShellFileSystemFolder item) => ItemsList.IndexOf(item);

    public bool Remove(ShellFileSystemFolder item)
    {
        if (item == null!) { throw new ArgumentNullException("item"); }

        try
        {
            nativeShellLibrary.RemoveFolder(item.NativeShellItem);
            nativeShellLibrary.Commit();
        }
        catch (COMException)
        {
            return false;
        }

        return true;
    }

    public bool Remove(string folderPath)
    {
        var item = ShellFileSystemFolder.FromFolderPath(folderPath);
        return Remove(item);
    }

    void ICollection<ShellFileSystemFolder>.CopyTo(ShellFileSystemFolder[] array, int arrayIndex) => throw new NotImplementedException();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => ItemsList.GetEnumerator();

    void IList<ShellFileSystemFolder>.Insert(int index, ShellFileSystemFolder item) =>
        throw new NotImplementedException();

    void IList<ShellFileSystemFolder>.RemoveAt(int index) =>
        throw new NotImplementedException();

    internal static ShellLibrary FromShellItem(IShellItem nativeShellItem, bool isReadOnly)
    {
        OsVersionHelper.ThrowIfNotWin7();

        var nativeShellLibrary = (INativeShellLibrary)new ShellLibraryCoClass();

        var flags = isReadOnly ?
                AccessModes.Read :
                AccessModes.ReadWrite;

        nativeShellLibrary.LoadLibraryFromItem(nativeShellItem, flags);

        ShellLibrary library = new(nativeShellLibrary)
        {
            nativeShellItem = (IShellItem2)nativeShellItem
        };

        return library;
    }

    protected override void Dispose(bool disposing)
    {
        if (nativeShellLibrary != null)
        {
            Marshal.ReleaseComObject(nativeShellLibrary);
            nativeShellLibrary = null!;
        }

        base.Dispose(disposing);
    }

    private static LibraryFolderType GetFolderTypefromGuid(Guid folderTypeGuid)
    {
        for (var i = 0; i < FolderTypesGuids.Length; i++)
        {
            if (folderTypeGuid.Equals(FolderTypesGuids[i]))
            {
                return (LibraryFolderType)i;
            }
        }
        throw new ArgumentOutOfRangeException("folderTypeGuid", LocalizedMessages.ShellLibraryInvalidFolderType);
    }

    private static void ShowManageLibraryUI(ShellLibrary shellLibrary, nint windowHandle, string title, string instruction, bool allowAllLocations)
    {
        var hr = 0;

        var staWorker = new Thread(() =>
        {
            hr = Shell32.SHShowManageLibraryUI(
                shellLibrary.NativeShellItem,
                windowHandle,
                title,
                instruction,
                allowAllLocations ?
                   LibraryManageDialogOptions.NonIndexableLocationWarning :
                   LibraryManageDialogOptions.Default);
        });

        staWorker.SetApartmentState(ApartmentState.STA);
        staWorker.Start();
        staWorker.Join();

        if (!CoreErrorHelper.Succeeded(hr)) { throw new ShellException(hr); }
    }

    private List<ShellFileSystemFolder> GetFolders()
    {
        var list = new List<ShellFileSystemFolder>();

        var shellItemArrayGuid = new Guid(ShellIIDGuid.IShellItemArray);

        var hr = nativeShellLibrary.GetFolders(LibraryFolderFilter.AllItems, ref shellItemArrayGuid, out var itemArray);

        if (!CoreErrorHelper.Succeeded(hr)) { return list; }

        itemArray.GetCount(out var count);

        for (uint i = 0; i < count; ++i)
        {
            itemArray.GetItemAt(i, out var shellItem);
            list.Add(new ShellFileSystemFolder((shellItem as IShellItem2)!));
        }

        if (itemArray != null)
        {
            Marshal.ReleaseComObject(itemArray);
            itemArray = null;
        }

        return list;
    }
}
