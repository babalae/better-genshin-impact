using OpenCvSharp;

namespace BetterGenshinImpact.Helpers.Extensions;

public static class RectExtension
{
    public static bool Contains(this Rect rect, double x, double y)
    {
        return (rect.X <= x && rect.Y <= y && rect.X + rect.Width > x && rect.Y + rect.Height > y);
    }
}
