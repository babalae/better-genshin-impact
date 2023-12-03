using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

[Guid("A6207B2E-7CDD-426A-951E-5E1CBC5AFEAD"), TypeLibType(4160)]
[ComImport]
public interface INetFwIcmpSettings
{
    [DispId(1)]
    bool AllowOutboundDestinationUnreachable
    {
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(2)]
    bool AllowRedirect
    {
        [DispId(2)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(2)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(3)]
    bool AllowInboundEchoRequest
    {
        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(4)]
    bool AllowOutboundTimeExceeded
    {
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(5)]
    bool AllowOutboundParameterProblem
    {
        [DispId(5)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(5)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(6)]
    bool AllowOutboundSourceQuench
    {
        [DispId(6)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(6)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(7)]
    bool AllowInboundRouterRequest
    {
        [DispId(7)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(7)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(8)]
    bool AllowInboundTimestampRequest
    {
        [DispId(8)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(8)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(9)]
    bool AllowInboundMaskRequest
    {
        [DispId(9)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(9)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }

    [DispId(10)]
    bool AllowOutboundPacketTooBig
    {
        [DispId(10)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
        [DispId(10)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        set;
    }
}
