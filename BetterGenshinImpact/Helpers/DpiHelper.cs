using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Helpers;

public class DpiHelper
{
    /// <summary>
    /// 所在显示器的纵向缩放比例。只能主线程调用
    /// </summary>
    /// <param name="gameWindowHandle">当前运行环境的游戏窗口句柄；为 0（截图器未启动）时按主窗口所在显示器计算</param>
    public static float GetScaleY(nint gameWindowHandle)
    {
        if (Environment.OSVersion.Version >= new Version(6, 3)
         && UIDispatcherHelper.MainWindow != null)
        {
            HWND hWnd = gameWindowHandle != 0
                ? gameWindowHandle
                : new WindowInteropHelper(Application.Current?.MainWindow).Handle;

            HMONITOR hMonitor = User32.MonitorFromWindow(hWnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
            SHCore.GetDpiForMonitor(hMonitor, SHCore.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out _, out uint dpiY);
            return dpiY / 96f;
        }

        HDC hdc = User32.GetDC(HWND.NULL);
        float scaleY = Gdi32.GetDeviceCaps(hdc, Gdi32.DeviceCap.LOGPIXELSY);
        _ = User32.ReleaseDC(0, hdc);
        return scaleY / 96f;
    }

    public static DpiScaleF GetScale(nint hWnd = 0)
    {
        if (hWnd != IntPtr.Zero)
        {
            HMONITOR hMonitor = User32.MonitorFromWindow(hWnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
            SHCore.GetDpiForMonitor(hMonitor, SHCore.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
            return new DpiScaleF(dpiX / 96f, dpiY / 96f);
        }

        using User32.SafeReleaseHDC hdc = User32.GetDC(hWnd);
        float scaleX = Gdi32.GetDeviceCaps(hdc, Gdi32.DeviceCap.LOGPIXELSX);
        float scaleY = Gdi32.GetDeviceCaps(hdc, Gdi32.DeviceCap.LOGPIXELSY);
        return new(scaleX / 96f, scaleY / 96f);
    }
}

[DebuggerDisplay("{ToString()}")]
public readonly struct DpiScaleF(float x = 1f, float y = 1f)
{
    private readonly float x = x;
    public float X => x;

    private readonly float y = y;
    public float Y => y;

    public DpiScaleF Reserve()
    {
        return new DpiScaleF(1f / x, 1f / y);
    }

    public override string ToString() => $"{X},{Y}";
}
