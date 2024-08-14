namespace Vision.WindowCapture;

public class WindowCaptureFactory
{
    public static string[] ModeNames()
    {
        return Enum.GetNames(typeof(CaptureModeEnum));
    }


    public static IWindowCapture Create(CaptureModeEnum mode)
    {
        return mode switch
        {
            CaptureModeEnum.BitBlt => new BitBlt.BitBltCapture(),
            CaptureModeEnum.WindowsGraphicsCapture => new GraphicsCapture.GraphicsCapture(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
