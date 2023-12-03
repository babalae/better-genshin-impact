using System;
using System.Collections.Generic;

namespace MicaSetup.Shell.Dialogs;

internal class ShellItemArray : IShellItemArray
{
    private readonly List<IShellItem> shellItemsList = new();

    internal ShellItemArray(IShellItem[] shellItems) => shellItemsList.AddRange(shellItems);

    public HResult BindToHandler(nint pbc, ref Guid rbhid, ref Guid riid, out nint ppvOut) => throw new NotSupportedException();

    public HResult EnumItems(out nint ppenumShellItems) => throw new NotSupportedException();

    public HResult GetAttributes(ShellItemAttributeOptions dwAttribFlags, ShellFileGetAttributesOptions sfgaoMask, out ShellFileGetAttributesOptions psfgaoAttribs) => throw new NotSupportedException();

    public HResult GetCount(out uint pdwNumItems)
    {
        pdwNumItems = (uint)shellItemsList.Count;
        return HResult.Ok;
    }

    public HResult GetItemAt(uint dwIndex, out IShellItem ppsi)
    {
        var index = (int)dwIndex;

        if (index < shellItemsList.Count)
        {
            ppsi = shellItemsList[index];
            return HResult.Ok;
        }
        else
        {
            ppsi = null!;
            return HResult.Fail;
        }
    }

    public HResult GetPropertyDescriptionList(ref PropertyKey keyType, ref Guid riid, out nint ppv) => throw new NotSupportedException();

    public HResult GetPropertyStore(int Flags, ref Guid riid, out nint ppv) => throw new NotSupportedException();
}
