namespace Fischless.WindowCapture;

public class WindowCaptureFactory
{
    public static string[] ModeNames()
    {
        return Enum.GetNames(typeof(CaptureModes));
    }

    public static IWindowCapture Create(CaptureModes mode)
    {
        return mode switch
        {
            CaptureModes.BitBlt => new BitBlt.BitBltCapture(),
            CaptureModes.WindowsGraphicsCapture => new Graphics.GraphicsCapture(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
