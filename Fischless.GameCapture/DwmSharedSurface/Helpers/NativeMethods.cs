using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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