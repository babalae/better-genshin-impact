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
            CaptureModes.WindowsGraphicsCapture => new Graphics.GraphicsCapture(),
            CaptureModes.DwmGetDxSharedSurface => new DwmSharedSurface.SharedSurfaceCapture(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
