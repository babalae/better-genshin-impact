using MicaSetup.Helper;
using MicaSetup.Natives;
using System.Security;
using System.Windows;
using System.Windows.Interop;

namespace MicaSetup.Design.Controls;

internal static class WindowSystemCommands
{
    [SecuritySafeCritical]
    public static void ShowSystemMenu(Window window, Point? screenLocation = null!)
    {
        if (screenLocation == null)
        {
            if (User32.GetCursorPos(out POINT pt))
            {
                SystemCommands.ShowSystemMenu(window, new Point(DpiHelper.CalcDPiX(pt.X), DpiHelper.CalcDPiY(pt.Y)));
            }
        }
        else
        {
            SystemCommands.ShowSystemMenu(window, new Point(DpiHelper.CalcDPiX(screenLocation.Value.X), DpiHelper.CalcDPiY(screenLocation.Value.Y)));
        }
    }

    [SecuritySafeCritical]
    public static void CloseWindow(Window window)
    {
        SystemCommands.CloseWindow(window);
    }

    [SecuritySafeCritical]
    public static void MaximizeWindow(Window window)
    {
        SystemCommands.MaximizeWindow(window);
    }

    [SecuritySafeCritical]
    public static void MinimizeWindow(Window window)
    {
        SystemCommands.MinimizeWindow(window);
    }

    [SecuritySafeCritical]
    public static void RestoreWindow(Window window)
    {
        SystemCommands.RestoreWindow(window);
        window.WindowStyle = WindowStyle.SingleBorderWindow;
    }

    [SecuritySafeCritical]
    public static void FastRestoreWindow(Window window, bool force = false)
    {
        _ = User32.PostMessage(new WindowInteropHelper(window).Handle, (int)User32.WindowMessage.WM_NCLBUTTONDOWN, (int)User32.HitTestValues.HTCAPTION, 0);
        window.WindowStyle = WindowStyle.SingleBorderWindow;
    }

    [SecuritySafeCritical]
    public static void MaximizeOrRestoreWindow(Window window)
    {
        if (window.WindowState == WindowState.Maximized)
        {
            RestoreWindow(window);
        }
        else
        {
            MaximizeWindow(window);
        }
    }
}
