using System.ComponentModel;

namespace Fischless.GameCapture;

public enum CaptureModes
{
    [Description("BitBlt（稳定）")]
    BitBlt,
    
    [Description("BitBlt（极速）")]
    BitBltNew,
    
    [Description("WindowsGraphicsCapture")]
    WindowsGraphicsCapture,
    
    [Description("DwmGetDxSharedSurface")]
    DwmGetDxSharedSurface
}
