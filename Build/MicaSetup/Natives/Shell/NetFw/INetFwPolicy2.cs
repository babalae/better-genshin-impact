using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

[Guid("98325047-C671-4174-8D81-DEFCD3F03186"), TypeLibType(4160)]
[ComImport]
public interface INetFwPolicy2
{
    [DispId(1)]
    int CurrentProfileTypes
    {
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
    }

    [DispId(2)]
    bool FirewallEnabled
    {
        [DispId(2)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(2)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(3)]
    object ExcludedInterfaces
    {
        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.Struct)]
        get;
        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.Struct)]
        set;
    }

    [DispId(4)]
    bool BlockAllInboundTraffic
    {
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(5)]
    bool NotificationsDisabled
    {
        [DispId(5)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(5)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(6)]
    bool UnicastResponsesToMulticastBroadcastDisabled
    {
        [DispId(6)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(6)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(7)]
    INetFwRules Rules
    {
        [DispId(7)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.Interface)]
        get;
    }

    [DispId(8)]
    INetFwServiceRestriction ServiceRestriction
    {
        [DispId(8)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.Interface)]
        get;
    }

    [DispId(12)]
    NET_FW_ACTION DefaultInboundAction
    {
        [DispId(12)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(12)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(13)]
    NET_FW_ACTION DefaultOutboundAction
    {
        [DispId(13)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(13)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(14)]
    bool IsRuleGroupCurrentlyEnabled
    {
        [DispId(14)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
    }

    [DispId(15)]
    NET_FW_MODIFY_STATE LocalPolicyModifyState
    {
        [DispId(15)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
    }

    [DispId(9)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    void EnableRuleGroup([In] int profileTypesBitmask, [MarshalAs(UnmanagedType.BStr)][In] string group, [In] bool enable);

    [DispId(10)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    bool IsRuleGroupEnabled([In] int profileTypesBitmask, [MarshalAs(UnmanagedType.BStr)][In] string group);

    [DispId(11)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    void RestoreLocalFirewallDefaults();
}
