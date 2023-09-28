namespace Vision.WindowCapture;

public static class CaptureModeExtensions
{
    public static CaptureMode ToCaptureMode(this string modeName)
    {
        return (CaptureMode) Enum.Parse(typeof(CaptureMode), modeName);
    }
}
