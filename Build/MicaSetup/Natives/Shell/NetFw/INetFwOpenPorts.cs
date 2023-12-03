using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

#pragma warning disable CS0108

[Guid("C0E9D7FA-E07E-430A-B19A-090CE82D92E2"), TypeLibType(4160)]
[ComImport]
public interface INetFwOpenPorts : IEnumerable
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
    void Add([MarshalAs(UnmanagedType.Interface)][In] INetFwOpenPort Port);

    [DispId(3)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    void Remove([In] int portNumber, [In] NET_FW_IP_PROTOCOL ipProtocol);

    [DispId(4)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    [return: MarshalAs(UnmanagedType.Interface)]
    INetFwOpenPort Item([In] int portNumber, [In] NET_FW_IP_PROTOCOL ipProtocol);

    [DispId(-4), TypeLibFunc(1)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    [return: MarshalAs(UnmanagedType.CustomMarshaler, MarshalType = "System.Runtime.InteropServices.CustomMarshalers.EnumeratorToEnumVariantMarshaler")]
    IEnumerator GetEnumerator();
}
