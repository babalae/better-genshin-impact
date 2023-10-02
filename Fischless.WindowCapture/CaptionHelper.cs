using System.Windows;
using Vanara.PInvoke;

namespace Fischless.WindowCapture;

internal static class CaptionHelper
{
    public static bool IsFullScreenMode(nint hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        int exStyle = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);

        if ((exStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0)
        {
            return  true;
        }
        return false;
    }

    public static int GetSystemCaptionHeight()
    {
        return (int)Math.Round(SystemParameters.CaptionHeight * DpiHelper.ScaleY);
    }
}
