using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

[Guid("B5E64FFA-C2C5-444E-A301-FB5E00018050"), TypeLibType(4160)]
[ComImport]
public interface INetFwAuthorizedApplication
{
    [DispId(1)]
    string Name
    {
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(2)]
    string ProcessImageFileName
    {
        [DispId(2)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(2)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(3)]
    NET_FW_IP_VERSION IpVersion
    {
        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(4)]
    NET_FW_SCOPE Scope
    {
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(5)]
    string RemoteAddresses
    {
        [DispId(5)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(5)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(6)]
    bool Enabled
    {
        [DispId(6)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(6)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }
}
