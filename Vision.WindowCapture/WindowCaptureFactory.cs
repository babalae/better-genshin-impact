namespace Vision.WindowCapture;

public class WindowCaptureFactory
{
    public static string[] ModeNames()
    {
        return Enum.GetNames(typeof(CaptureMode));
    }


    public static IWindowCapture Create(CaptureMode mode)
    {
        switch (mode)
        {
            case CaptureMode.BitBlt:
                return new BitBlt.BitBltCapture();
            case CaptureMode.WindowsGraphicsCapture:
                return new GraphicsCapture.GraphicsCapture();
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }
}
