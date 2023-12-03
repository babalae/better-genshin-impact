using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.NetFw;

[Guid("71881699-18F4-458B-B892-3FFCE5E07F75"), TypeLibType(4160)]
[ComImport]
public interface INetFwProduct
{
    [DispId(1)]
    object RuleCategories
    {
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.Struct)]
        get;
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [param: MarshalAs(UnmanagedType.Struct)]
        set;
    }

    [DispId(2)]
    string DisplayName
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
    string PathToSignedProductExe
    {
        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
    }
}
