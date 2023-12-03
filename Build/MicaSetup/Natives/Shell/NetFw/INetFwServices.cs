using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

#pragma warning disable CS0108

[Guid("79649BB4-903E-421B-94C9-79848E79F6EE"), TypeLibType(4160)]
[ComImport]
public interface INetFwServices : IEnumerable
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
    [return: MarshalAs(UnmanagedType.Interface)]
    INetFwService Item([In] NET_FW_SERVICE_TYPE svcType);

    [DispId(-4), TypeLibFunc(1)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    [return: MarshalAs(UnmanagedType.CustomMarshaler, MarshalType = "System.Runtime.InteropServices.CustomMarshalers.EnumeratorToEnumVariantMarshaler")]
    IEnumerator GetEnumerator();
}
