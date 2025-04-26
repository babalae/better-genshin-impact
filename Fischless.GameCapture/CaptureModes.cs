using System.ComponentModel;

namespace Fischless.GameCapture;

public enum CaptureModes
{
    // [Description("BitBlt（稳定）")]
    // BitBlt,
    
    [Description("BitBlt")]
    BitBlt,
    
    [Description("WindowsGraphicsCapture")]
    WindowsGraphicsCapture,
    
    [Description("DwmGetDxSharedSurface")]
    DwmGetDxSharedSurface
}
