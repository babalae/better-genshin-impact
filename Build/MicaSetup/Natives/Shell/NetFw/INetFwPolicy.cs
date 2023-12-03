using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

[Guid("D46D2478-9AC9-4008-9DC7-5563CE5536CC"), TypeLibType(4160)]
[ComImport]
public interface INetFwPolicy
{
    [DispId(1)]
    INetFwProfile CurrentProfile
    {
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.Interface)]
        get;
    }

    [DispId(2)]
    [MethodImpl(MethodImplOptions.InternalCall)]
    [return: MarshalAs(UnmanagedType.Interface)]
    INetFwProfile GetProfileByType([In] NET_FW_PROFILE_TYPE profileType);
}
