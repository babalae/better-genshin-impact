using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

[Guid("E0483BA0-47FF-4D9C-A6D6-7741D0B195F7"), TypeLibType(4160)]
[ComImport]
public interface INetFwOpenPort
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
    NET_FW_IP_VERSION IpVersion
    {
        [DispId(2)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(2)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(3)]
    NET_FW_IP_PROTOCOL Protocol
    {
        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(4)]
    int Port
    {
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(5)]
    NET_FW_SCOPE Scope
    {
        [DispId(5)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(5)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(6)]
    string RemoteAddresses
    {
        [DispId(6)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(6)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(7)]
    bool Enabled
    {
        [DispId(7)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(7)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(8)]
    bool BuiltIn
    {
        [DispId(8)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
    }
}
