using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

[Guid("D4BECDDF-6F73-4A83-B832-9C66874CD20E"), TypeLibType(4160)]
[ComImport]
public interface INetFwRemoteAdminSettings
{
    [DispId(1)]
    NET_FW_IP_VERSION IpVersion
    {
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(2)]
    NET_FW_SCOPE Scope
    {
        [DispId(2)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(2)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(3)]
    string RemoteAddresses
    {
        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(4)]
    bool Enabled
    {
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }
}
