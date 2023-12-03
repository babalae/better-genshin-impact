using System.Runtime.InteropServices;

namespace MicaSetup.Natives;

public static class DwmApi
{
    [DllImport(Lib.DwmApi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int DwmSetWindowAttribute(nint hwnd, DWMWINDOWATTRIBUTE dwAttribute, ref int pvAttribute, int cbAttribute);

    public static int DwmSetWindowAttribute(nint hwnd, DWMWINDOWATTRIBUTE dwAttribute, int pvAttribute, int cbAttribute)
    {
        return DwmSetWindowAttribute(hwnd, dwAttribute, ref pvAttribute, cbAttribute);
    }

    public enum DWM_SYSTEMBACKDROP_TYPE
    {
        /// <summary>
        /// The default. Let the Desktop Window Manager (DWM) automatically decide the system-drawn backdrop material for this window.
        /// </summary>
        DWMSBT_AUTO,

        /// <summary>Don't draw any system backdrop.</summary>
        DWMSBT_NONE,

        /// <summary>Draw the backdrop material effect corresponding to a long-lived window.</summary>
        DWMSBT_MAINWINDOW,

        /// <summary>Draw the backdrop material effect corresponding to a transient window.</summary>
        DWMSBT_TRANSIENTWINDOW,

        /// <summary>Draw the backdrop material effect corresponding to a window with a tabbed title bar.</summary>
        DWMSBT_TABBEDWINDOW,
    }
}
