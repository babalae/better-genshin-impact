using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

#pragma warning disable CS0108

[Guid("9C4C6277-5027-441E-AFAE-CA1F542DA009"), TypeLibType(4160)]
[ComImport]
public interface INetFwRules : IEnumerable
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
    void Add([MarshalAs(UnmanagedType.Interface)][In] INetFwRule rule);

    [DispId(3)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    void Remove([MarshalAs(UnmanagedType.BStr)][In] string Name);

    [DispId(4)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    [return: MarshalAs(UnmanagedType.Interface)]
    INetFwRule Item([MarshalAs(UnmanagedType.BStr)][In] string Name);

    [DispId(-4), TypeLibFunc(1)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    [return: MarshalAs(UnmanagedType.CustomMarshaler, MarshalType = "System.Runtime.InteropServices.CustomMarshalers.EnumeratorToEnumVariantMarshaler")]
    IEnumerator GetEnumerator();
}
