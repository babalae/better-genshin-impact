namespace Fischless.GameCapture;

public static class CaptureModeExtensions
{
    public static CaptureModes ToCaptureMode(this string? modeName)
    {
        if (modeName.TryToCaptureMode(out var mode))
        {
            return mode;
        }

        throw new ArgumentException($"未知的截图模式：{modeName}", nameof(modeName));
    }

    public static bool TryToCaptureMode(this string? modeName, out CaptureModes mode)
    {
        return Enum.TryParse(modeName, true, out mode) && Enum.IsDefined(mode);
    }
}
