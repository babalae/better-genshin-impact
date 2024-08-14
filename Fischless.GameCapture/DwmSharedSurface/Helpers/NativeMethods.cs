using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace Fischless.GameCapture.DwmSharedSurface.Helpers;

internal class NativeMethods
{

    public delegate bool DwmGetDxSharedSurfaceDelegate(IntPtr hWnd, out IntPtr phSurface, out long pAdapterLuid, out long pFmtWindow, out long pPresentFlags, out long pWin32KUpdateId);

    public static DwmGetDxSharedSurfaceDelegate DwmGetDxSharedSurface;

    static NativeMethods()
    {
        var ptr = Kernel32.GetProcAddress(Kernel32.GetModuleHandle("user32"), "DwmGetDxSharedSurface");
        DwmGetDxSharedSurface = Marshal.GetDelegateForFunctionPointer<DwmGetDxSharedSurfaceDelegate>(ptr);
    }
}