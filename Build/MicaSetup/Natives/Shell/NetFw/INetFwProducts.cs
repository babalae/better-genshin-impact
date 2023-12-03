using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

#pragma warning disable CS0108

[Guid("39EB36E0-2097-40BD-8AF2-63A13B525362"), TypeLibType(4160)]
[ComImport]
public interface INetFwProducts : IEnumerable
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
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object Register([MarshalAs(UnmanagedType.Interface)][In] INetFwProduct product);

    [DispId(3)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    [return: MarshalAs(UnmanagedType.Interface)]
    INetFwProduct Item([In] int index);

    [DispId(-4), TypeLibFunc(1)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    [return: MarshalAs(UnmanagedType.CustomMarshaler, MarshalType = "System.Runtime.InteropServices.CustomMarshalers.EnumeratorToEnumVariantMarshaler")]
    IEnumerator GetEnumerator();
}
