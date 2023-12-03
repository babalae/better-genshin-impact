using MicaSetup.Helper;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

public abstract class ShellObject : IDisposable, IEquatable<ShellObject>
{
    internal IShellItem2 nativeShellItem;

    private string _internalName;

    private string _internalParsingName;

    private nint _internalPIDL = 0;

    private int? hashValue;

    private ShellObject parentShellObject;

    private ShellThumbnail thumbnail;

    internal ShellObject()
    {
    }

    internal ShellObject(IShellItem2 shellItem) => nativeShellItem = shellItem;

    ~ShellObject()
    {
        Dispose(false);
    }

    public static bool IsPlatformSupported => OsVersionHelper.IsWindowsVista_OrGreater;

    public bool IsFileSystemObject
    {
        get
        {
            try
            {
                NativeShellItem.GetAttributes(ShellFileGetAttributesOptions.FileSystem, out var sfgao);
                return (sfgao & ShellFileGetAttributesOptions.FileSystem) != 0;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }
    }

    public bool IsLink
    {
        get
        {
            try
            {
                NativeShellItem.GetAttributes(ShellFileGetAttributesOptions.Link, out var sfgao);
                return (sfgao & ShellFileGetAttributesOptions.Link) != 0;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }
    }

    public virtual string Name
    {
        get
        {
            if (_internalName == null && NativeShellItem != null)
            {
                var hr = NativeShellItem.GetDisplayName(ShellItemDesignNameOptions.Normal, out var pszString);
                if (hr == HResult.Ok && pszString != 0)
                {
                    _internalName = Marshal.PtrToStringAuto(pszString);

                    Marshal.FreeCoTaskMem(pszString);
                }
            }
            return _internalName!;
        }

        protected set => _internalName = value;
    }

    public ShellObject Parent
    {
        get
        {
            if (parentShellObject == null! && NativeShellItem2 != null)
            {
                var hr = NativeShellItem2.GetParent(out var parentShellItem);

                if (hr == HResult.Ok && parentShellItem != null)
                {
                    parentShellObject = ShellObjectFactory.Create(parentShellItem);
                }
                else if (hr == HResult.NoObject)
                {
                    return null!;
                }
                else
                {
                    throw new ShellException(hr);
                }
            }

            return parentShellObject!;
        }
    }

    public virtual string ParsingName
    {
        get
        {
            if (_internalParsingName == null && nativeShellItem != null)
            {
                _internalParsingName = ShellHelper.GetParsingName(nativeShellItem);
            }
            return _internalParsingName ?? string.Empty;
        }
        protected set => _internalParsingName = value;
    }

    public ShellThumbnail Thumbnail
    {
        get
        {
            thumbnail ??= new ShellThumbnail(this);
            return thumbnail;
        }
    }

    internal IPropertyStore NativePropertyStore { get; set; }

    internal virtual IShellItem NativeShellItem => NativeShellItem2;

    internal virtual IShellItem2 NativeShellItem2
    {
        get
        {
            if (nativeShellItem == null && ParsingName != null)
            {
                var guid = new Guid(ShellIIDGuid.IShellItem2);
                var retCode = Shell32.SHCreateItemFromParsingName(ParsingName, 0, ref guid, out nativeShellItem);

                if (nativeShellItem == null || !CoreErrorHelper.Succeeded(retCode))
                {
                    throw new ShellException(LocalizedMessages.ShellObjectCreationFailed, Marshal.GetExceptionForHR(retCode));
                }
            }
            return nativeShellItem!;
        }
    }

    internal virtual nint PIDL
    {
        get
        {
            if (_internalPIDL == 0 && NativeShellItem != null)
            {
                _internalPIDL = ShellHelper.PidlFromShellItem(NativeShellItem);
            }

            return _internalPIDL;
        }
        set => _internalPIDL = value;
    }

    public static ShellObject FromParsingName(string parsingName) => ShellObjectFactory.Create(parsingName);

    public static bool operator !=(ShellObject leftShellObject, ShellObject rightShellObject) => !(leftShellObject == rightShellObject);

    public static bool operator ==(ShellObject leftShellObject, ShellObject rightShellObject)
    {
        if (leftShellObject is null)
        {
            return rightShellObject is null;
        }
        return leftShellObject.Equals(rightShellObject);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public bool Equals(ShellObject other)
    {
        var areEqual = false;

        if (other != null!)
        {
            var ifirst = NativeShellItem;
            var isecond = other.NativeShellItem;
            if (ifirst != null && isecond != null)
            {
                var hr = ifirst.Compare(
                    isecond, SICHINTF.SICHINT_ALLFIELDS, out var result);

                areEqual = (hr == HResult.Ok) && (result == 0);
            }
        }

        return areEqual;
    }

    public override bool Equals(object obj) => Equals((obj as ShellObject)!);

    public virtual string GetDisplayName(DisplayNameType displayNameType)
    {
        string returnValue = null!;
        NativeShellItem2?.GetDisplayName((ShellItemDesignNameOptions)displayNameType, out returnValue);
        return returnValue;
    }

    public override int GetHashCode()
    {
        if (!hashValue.HasValue)
        {
            var size = Shell32.ILGetSize(PIDL);
            if (size != 0)
            {
                var pidlData = new byte[size];
                Marshal.Copy(PIDL, pidlData, 0, (int)size);

                const int p = 16777619;
                int hash = -2128831035;

                for (int i = 0; i < pidlData.Length; i++)
                    hash = (hash ^ pidlData[i]) * p;

                hashValue = hash;
            }
            else
            {
                hashValue = 0;
            }
        }
        return hashValue.Value;
    }

    public override string ToString() => Name;

    public void Update(IBindCtx bindContext)
    {
        var hr = HResult.Ok;

        if (NativeShellItem2 != null)
        {
            hr = NativeShellItem2.Update(bindContext);
        }

        if (CoreErrorHelper.Failed(hr))
        {
            throw new ShellException(hr);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _internalName = null!;
            _internalParsingName = null!;
            thumbnail = null!;
            parentShellObject = null!;
        }

        if (_internalPIDL != 0)
        {
            Shell32.ILFree(_internalPIDL);
            _internalPIDL = 0;
        }

        if (nativeShellItem != null)
        {
            Marshal.ReleaseComObject(nativeShellItem);
            nativeShellItem = null!;
        }

        if (NativePropertyStore != null)
        {
            Marshal.ReleaseComObject(NativePropertyStore);
            NativePropertyStore = null!;
        }
    }
}
