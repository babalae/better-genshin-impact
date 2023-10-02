using System.Windows.Interop;
using Vanara.PInvoke;
using Application = System.Windows.Application;

namespace Fischless.WindowCapture;

internal static class DpiHelper
{
    public static float ScaleY => GetScaleY();

    private static float GetScaleY()
    {
        if (Environment.OSVersion.Version >= new Version(6, 3)
         && Application.Current?.MainWindow != null)
        {
            HWND hWnd = new WindowInteropHelper(Application.Current?.MainWindow).Handle;
            HMONITOR hMonitor = User32.MonitorFromWindow(hWnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
            _ = SHCore.GetDpiForMonitor(hMonitor, SHCore.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out _, out uint dpiY);
            return dpiY / 96f;
        }

        HDC hdc = User32.GetDC(HWND.NULL);
        float scaleY = Gdi32.GetDeviceCaps(hdc, Gdi32.DeviceCap.LOGPIXELSY);
        _ = User32.ReleaseDC(HWND.NULL, hdc);
        return scaleY / 96f;
    }
}
