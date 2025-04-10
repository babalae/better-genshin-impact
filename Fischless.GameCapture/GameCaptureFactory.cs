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
            CaptureModes.BitBltNew => new BitBlt.BitBltCapture(),
            CaptureModes.BitBlt => new BitBlt.BitBltOldCapture(),
            CaptureModes.WindowsGraphicsCapture => new Graphics.GraphicsCapture(),
            CaptureModes.DwmGetDxSharedSurface => new DwmSharedSurface.SharedSurfaceCapture(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
