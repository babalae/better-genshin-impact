using MicaSetup.Helper;
using MicaSetup.Natives;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MicaSetup.Design.Controls;

public static class WindowDarkMode
{
    public static bool ApplyWindowDarkMode(nint hWnd)
    {
        if (hWnd == 0x00 || !User32.IsWindow(hWnd))
        {
            return false;
        }

        var dwAttribute = DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE;

        if (!OsVersionHelper.IsWindows11_22523_OrGreater)
        {
            dwAttribute = DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD;
        }

        _ = DwmApi.DwmSetWindowAttribute(
            hWnd,
            dwAttribute,
            0x1,
            Marshal.SizeOf(typeof(int))
        );

        return true;
    }

    public static bool RemoveWindowDarkMode(nint handle)
    {
        if (handle == 0x00 || !User32.IsWindow(handle))
        {
            return false;
        }

        var dwAttribute = DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE;

        if (!OsVersionHelper.IsWindows11_22523_OrGreater)
        {
            dwAttribute = DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD;
        }

        _ = DwmApi.DwmSetWindowAttribute(
            handle,
            dwAttribute,
            0x0,
            Marshal.SizeOf(typeof(int))
        );
        return true;
    }

    public static bool RemoveBackground(nint hWnd)
    {
        if (hWnd == 0x00 || !User32.IsWindow(hWnd))
        {
            return false;
        }

        var windowSource = HwndSource.FromHwnd(hWnd);

        if (windowSource?.RootVisual is Window window)
        {
            return RemoveBackground(window);
        }
        return false;
    }

    public static bool RemoveBackground(Window window)
    {
        if (window == null)
        {
            return false;
        }

        window.Background = Brushes.Transparent;

        nint windowHandle = new WindowInteropHelper(window).Handle;

        if (windowHandle == 0x00)
        {
            return false;
        }

        var windowSource = HwndSource.FromHwnd(windowHandle);

        if (windowSource?.Handle.ToInt32() != 0x00 && windowSource?.CompositionTarget != null)
        {
            windowSource.CompositionTarget.BackgroundColor = Colors.Transparent;
        }

        return true;
    }
}
