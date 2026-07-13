using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace Fischless.GameCapture.Graphics.Helpers;

public static class CaptureHelper
{
    static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    interface IInitializeWithWindow
    {
        void Initialize(
            nint hWnd);
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(
            [In] nint window,
            [In] ref Guid iid);

        nint CreateForMonitor(
            [In] nint monitor,
            [In] ref Guid iid);
    }

    public static void SetWindow(this GraphicsCapturePicker picker, nint hWnd)
    {
        var interop = picker.As<IInitializeWithWindow>();
        interop.Initialize(hWnd);
    }

    public static GraphicsCaptureItem CreateItemForWindow(nint hWnd)
    {
        var factory = WinrtModule.GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        var itemPointer = interop.CreateForWindow(hWnd, GraphicsCaptureItemGuid);
        return GraphicsCaptureItem.FromAbi(itemPointer);
    }
}
