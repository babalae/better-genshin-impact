namespace Fischless.GameCapture;

public class GameCaptureFactory
{
    public static string[] ModeNames()
    {
        return Enum.GetNames(typeof(CaptureModes));
    }

    public static IGameCapture Create(CaptureModes mode)
    {
        return mode switch
        {
            CaptureModes.BitBlt => new BitBlt.BitBltCapture(),
            CaptureModes.DwmGetDxSharedSurface => new DwmSharedSurface.SharedSurfaceCapture(),
            CaptureModes.WindowsGraphicsCapture => new Graphics.GraphicsCapture(),
            CaptureModes.WindowsGraphicsCaptureHdr => new Graphics.GraphicsCapture(true),
            CaptureModes.WindowsGraphicsCaptureV2 => new Graphics.GraphicsCaptureV2(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
