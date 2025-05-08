using System.ComponentModel;

namespace Fischless.GameCapture;

public enum CaptureModes
{
    [Description("BitBlt")]
    BitBlt,
    
    [Description("WindowsGraphicsCapture")]
    WindowsGraphicsCapture,
    
    [Description("DwmGetDxSharedSurface")]
    DwmGetDxSharedSurface
}
