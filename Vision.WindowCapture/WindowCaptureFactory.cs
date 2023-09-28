namespace Vision.WindowCapture;

public class WindowCaptureFactory
{
    public static string[] ModeNames()
    {
        return Enum.GetNames(typeof(CaptureModeEnum));
    }


    public static IWindowCapture Create(CaptureModeEnum mode)
    {
        switch (mode)
        {
            case CaptureModeEnum.BitBlt:
                return new BitBlt.BitBltCapture();
            case CaptureModeEnum.WindowsGraphicsCapture:
                return new GraphicsCapture.GraphicsCapture();
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }
}
