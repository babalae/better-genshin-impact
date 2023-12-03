using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

public class ShellObjectCollection : IEnumerable, IDisposable, IList<ShellObject>
{
    private readonly List<ShellObject> content = new();
    private readonly bool readOnly;
    private bool isDisposed;

    public ShellObjectCollection()
    {
    }

    internal ShellObjectCollection(IShellItemArray iArray, bool readOnly)
    {
        this.readOnly = readOnly;

        if (iArray != null)
        {
            try
            {
                iArray.GetCount(out var itemCount);
                content.Capacity = (int)itemCount;
                for (uint index = 0; index < itemCount; index++)
                {
                    iArray.GetItemAt(index, out var iShellItem);
                    content.Add(ShellObjectFactory.Create(iShellItem));
                }
            }
            finally
            {
                Marshal.ReleaseComObject(iArray);
            }
        }
    }

    ~ShellObjectCollection()
    {
        Dispose(false);
    }

    public bool IsReadOnly => readOnly;

    public int Count => content.Count;

    int ICollection<ShellObject>.Count => content.Count;

    public ShellObject this[int index]
    {
        get => content[index];
        set
        {
            if (readOnly)
            {
                throw new InvalidOperationException(LocalizedMessages.ShellObjectCollectionInsertReadOnly);
            }

            content[index] = value;
        }
    }

    public static ShellObjectCollection FromDataObject(System.Runtime.InteropServices.ComTypes.IDataObject dataObject)
    {
        var iid = new Guid(ShellIIDGuid.IShellItemArray);
        Shell32.SHCreateShellItemArrayFromDataObject(dataObject, ref iid, out var shellItemArray);
        return new ShellObjectCollection(shellItemArray, true);
    }

    public void Add(ShellObject item)
    {
        if (readOnly)
        {
            throw new InvalidOperationException(LocalizedMessages.ShellObjectCollectionInsertReadOnly);
        }

        content.Add(item);
    }

    public MemoryStream BuildShellIDList()
    {
        if (content.Count == 0)
        {
            throw new InvalidOperationException(LocalizedMessages.ShellObjectCollectionEmptyCollection);
        }

        var mstream = new MemoryStream();
        try
        {
            var bwriter = new BinaryWriter(mstream);

            var itemCount = (uint)(content.Count + 1);

            var idls = new nint[itemCount];

            for (var index = 0; index < itemCount; index++)
            {
                if (index == 0)
                {
                    idls[index] = ((ShellObject)KnownFolders.Desktop).PIDL;
                }
                else
                {
                    idls[index] = content[index - 1].PIDL;
                }
            }

            var offsets = new uint[itemCount + 1];
            for (var index = 0; index < itemCount; index++)
            {
                if (index == 0)
                {
                    offsets[0] = (uint)(sizeof(uint) * (offsets.Length + 1));
                }
                else
                {
                    offsets[index] = offsets[index - 1] + Shell32.ILGetSize(idls[index - 1]);
                }
            }

            bwriter.Write(content.Count);
            foreach (var offset in offsets)
            {
                bwriter.Write(offset);
            }

            foreach (var idl in idls)
            {
                var data = new byte[Shell32.ILGetSize(idl)];
                Marshal.Copy(idl, data, 0, data.Length);
                bwriter.Write(data, 0, data.Length);
            }
        }
        catch
        {
            mstream.Dispose();
            throw;
        }
        return mstream;
    }

    public void Clear()
    {
        if (readOnly)
        {
            throw new InvalidOperationException(LocalizedMessages.ShellObjectCollectionRemoveReadOnly);
        }

        content.Clear();
    }

    public bool Contains(ShellObject item) => content.Contains(item);

    public void CopyTo(ShellObject[] array, int arrayIndex)
    {
        if (array == null) { throw new ArgumentNullException("array"); }
        if (array.Length < arrayIndex + content.Count)
        {
            throw new ArgumentException(LocalizedMessages.ShellObjectCollectionArrayTooSmall, "array");
        }

        for (var index = 0; index < content.Count; index++)
        {
            array[index + arrayIndex] = content[index];
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public System.Collections.IEnumerator GetEnumerator()
    {
        foreach (var obj in content)
        {
            yield return obj;
        }
    }

    public int IndexOf(ShellObject item) => content.IndexOf(item);

    public void Insert(int index, ShellObject item)
    {
        if (readOnly)
        {
            throw new InvalidOperationException(LocalizedMessages.ShellObjectCollectionInsertReadOnly);
        }

        content.Insert(index, item);
    }

    public bool Remove(ShellObject item)
    {
        if (readOnly)
        {
            throw new InvalidOperationException(LocalizedMessages.ShellObjectCollectionRemoveReadOnly);
        }

        return content.Remove(item);
    }

    public void RemoveAt(int index)
    {
        if (readOnly)
        {
            throw new InvalidOperationException(LocalizedMessages.ShellObjectCollectionRemoveReadOnly);
        }

        content.RemoveAt(index);
    }

    IEnumerator<ShellObject> IEnumerable<ShellObject>.GetEnumerator()
    {
        foreach (var obj in content)
        {
            yield return obj;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (isDisposed == false)
        {
            if (disposing)
            {
                foreach (var shellObject in content)
                {
                    shellObject.Dispose();
                }

                content.Clear();
            }

            isDisposed = true;
        }
    }
}
