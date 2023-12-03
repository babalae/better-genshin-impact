using MicaSetup.Natives;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace MicaSetup.Shell.Dialogs;

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("b7d14566-0509-4cce-a71f-0a554233bd9b")]
public interface IInitializeWithFile
{
    void Initialize([MarshalAs(UnmanagedType.LPWStr)] string filePath, AccessModes fileMode);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841")]
public interface IInitializeWithItem
{
    void Initialize([In, MarshalAs(UnmanagedType.IUnknown)] object shellItem, AccessModes accessMode);
}

[SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
[ComVisible(true)]
[Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithStream
{
    void Initialize(IStream stream, AccessModes fileMode);
}

[ComVisible(true)]
[Guid("e357fccd-a995-4576-b01f-234630154e96")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IThumbnailProvider
{
    [SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "2#"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "1#")]
    void GetThumbnail(uint squareLength, out nint bitmapHandle, out uint bitmapType);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("fc4801a3-2ba9-11cf-a229-00aa003d7352")]
internal interface IObjectWithSite
{
    void SetSite([In, MarshalAs(UnmanagedType.IUnknown)] object pUnkSite);

    void GetSite(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvSite);
}

[ComImport]
[Guid("00000114-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleWindow
{
    void GetWindow(out nint phwnd);

    void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("8895b1c6-b41f-4c1c-a562-0d564250836f")]
internal interface IPreviewHandler
{
    void SetWindow(nint hwnd, ref RECT rect);

    void SetRect(ref RECT rect);

    void DoPreview();

    void Unload();

    void SetFocus();

    void QueryFocus(out nint phwnd);

    [PreserveSig]
    HResult TranslateAccelerator(ref MSG pmsg);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("fec87aaf-35f9-447a-adb7-20234491401a")]
internal interface IPreviewHandlerFrame
{
    void GetWindowContext(nint pinfo);

    [PreserveSig]
    HResult TranslateAccelerator(ref MSG pmsg);
};

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("8327b13c-b63f-4b24-9b8a-d010dcc3f599")]
internal interface IPreviewHandlerVisuals
{
    void SetBackgroundColor(COLORREF color);

    void SetFont(ref LogFont plf);

    void SetTextColor(COLORREF color);
}

[StructLayout(LayoutKind.Sequential)]
internal struct COLORREF
{
    public uint Dword;

    public Color Color => Color.FromArgb(
                (int)(0x000000FFU & Dword),
                (int)(0x0000FF00U & Dword) >> 8,
                (int)(0x00FF0000U & Dword) >> 16);
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd;
    public int message;
    public nint wParam;
    public nint lParam;
    public int time;
    public int pt_x;
    public int pt_y;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal class LogFont
{
    internal int height;
    internal int width;
    internal int escapement;
    internal int orientation;
    internal int weight;
    internal byte italic;
    internal byte underline;
    internal byte strikeOut;
    internal byte charSet;
    internal byte outPrecision;
    internal byte clipPrecision;
    internal byte quality;
    internal byte pitchAndFamily;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    internal string lfFaceName = string.Empty;
}
