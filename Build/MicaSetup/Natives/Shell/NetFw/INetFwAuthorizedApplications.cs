using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

#pragma warning disable CS0108

[Guid("644EFD52-CCF9-486C-97A2-39F352570B30"), TypeLibType(4160)]
[ComImport]
public interface INetFwAuthorizedApplications : IEnumerable
{
    [DispId(1)]
    int Count
    {
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
    }

    [DispId(2)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    void Add([MarshalAs(UnmanagedType.Interface)][In] INetFwAuthorizedApplication app);

    [DispId(3)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    void Remove([MarshalAs(UnmanagedType.BStr)][In] string imageFileName);

    [DispId(4)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    [return: MarshalAs(UnmanagedType.Interface)]
    INetFwAuthorizedApplication Item([MarshalAs(UnmanagedType.BStr)][In] string imageFileName);

    [DispId(-4), TypeLibFunc(1)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    [return: MarshalAs(UnmanagedType.CustomMarshaler, MarshalType = "System.Runtime.InteropServices.CustomMarshalers.EnumeratorToEnumVariantMarshaler")]
    IEnumerator GetEnumerator();
}
