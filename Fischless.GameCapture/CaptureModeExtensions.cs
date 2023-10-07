namespace Fischless.GameCapture;

public static class CaptureModeExtensions
{
    public static CaptureModes ToCaptureMode(this string modeName)
    {
        return (CaptureModes)Enum.Parse(typeof(CaptureModes), modeName);
    }
}
