using MicaSetup.Natives;
using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

internal static class StockIconsNativeMethods
{
    [Flags]
    internal enum StockIconOptions
    {
        Large = 0x000000000,
        Small = 0x000000001,
        ShellSize = 0x000000004,
        Handle = 0x000000100,
        SystemIndex = 0x000004000,
        LinkOverlay = 0x000008000,
        Selected = 0x000010000,
    }

    [PreserveSig]
    [DllImport(Lib.Shell32, CharSet = CharSet.Unicode,
    ExactSpelling = true, SetLastError = false)]
    internal static extern HResult SHGetStockIconInfo(
        StockIconIdentifier identifier,
        StockIconOptions flags,
        ref StockIconInfo info);

    [StructLayoutAttribute(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StockIconInfo
    {
        internal uint StuctureSize;
        internal nint Handle;
        internal int ImageIndex;
        internal int Identifier;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string Path;
    }
}
