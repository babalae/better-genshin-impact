using System.ComponentModel;

namespace Fischless.GameCapture;

public enum CaptureModes
{
    [Description("BitBlt(稳定)")]
    BitBltOld,
    
    [Description("BitBlt(极速)")]
    BitBlt,
    
    [Description("WindowsGraphicsCapture")]
    WindowsGraphicsCapture,
    
    [Description("DwmGetDxSharedSurface")]
    DwmGetDxSharedSurface
}
