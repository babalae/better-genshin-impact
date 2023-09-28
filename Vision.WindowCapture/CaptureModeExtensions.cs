namespace Vision.WindowCapture;

public static class CaptureModeExtensions
{
    public static CaptureModeEnum ToCaptureMode(this string modeName)
    {
        return (CaptureModeEnum) Enum.Parse(typeof(CaptureModeEnum), modeName);
    }
}
