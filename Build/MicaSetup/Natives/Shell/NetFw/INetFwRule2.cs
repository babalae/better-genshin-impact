using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

#pragma warning disable CS0108
#pragma warning disable IDE1006

[Guid("9C27C8DA-189B-4DDE-89F7-8B39A316782C"), TypeLibType(4160)]
[ComImport]
public interface INetFwRule2 : INetFwRule
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
    string Description
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
    string ApplicationName
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
    string serviceName
    {
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(5)]
    int Protocol
    {
        [DispId(5)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(5)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(6)]
    string LocalPorts
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
    string RemotePorts
    {
        [DispId(7)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(7)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(8)]
    string LocalAddresses
    {
        [DispId(8)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(8)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(9)]
    string RemoteAddresses
    {
        [DispId(9)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(9)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(10)]
    string IcmpTypesAndCodes
    {
        [DispId(10)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(10)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(11)]
    NET_FW_RULE_DIRECTION Direction
    {
        [DispId(11)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(11)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(12)]
    object Interfaces
    {
        [DispId(12)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.Struct)]
        get;
        [DispId(12)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.Struct)]
        set;
    }

    [DispId(13)]
    string InterfaceTypes
    {
        [DispId(13)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(13)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(14)]
    bool Enabled
    {
        [DispId(14)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(14)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(15)]
    string Grouping
    {
        [DispId(15)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [DispId(15)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.BStr)]
        set;
    }

    [DispId(16)]
    int Profiles
    {
        [DispId(16)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(16)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(17)]
    bool EdgeTraversal
    {
        [DispId(17)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(17)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(18)]
    NET_FW_ACTION Action
    {
        [DispId(18)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(18)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(19)]
    int EdgeTraversalOptions
    {
        [DispId(19)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(19)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }
}
