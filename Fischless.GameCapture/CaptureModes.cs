using System.ComponentModel;

namespace Fischless.GameCapture;

public enum CaptureModes
{
    [Description("BitBlt")]
    [DefaultValue(0)]
    BitBlt = 0,

    [Description("DwmGetDxSharedSurface")]
    [DefaultValue(1)]
    DwmGetDxSharedSurface = 2,

    [Description("WindowsGraphicsCapture")]
    [DefaultValue(2)]
    WindowsGraphicsCapture = 1,

    [Description("WindowsGraphicsCapture（HDR）")]
    [DefaultValue(3)]
    WindowsGraphicsCaptureHdr = 3,
}
