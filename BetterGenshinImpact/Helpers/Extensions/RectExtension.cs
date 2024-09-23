using OpenCvSharp;

namespace BetterGenshinImpact.Helpers.Extensions;

public static class RectExtension
{
    public static bool Contains(this Rect rect, double x, double y)
    {
        return (rect.X <= x && rect.Y <= y && rect.X + rect.Width > x && rect.Y + rect.Height > y);
    }

    public static Rect Shrink(this Rect rect, int shrinkAmount)
    {
        return new Rect(rect.X + shrinkAmount, rect.Y + shrinkAmount, rect.Width - 2 * shrinkAmount, rect.Height - 2 * shrinkAmount);
    }

    public static Rect Shrink(this Rect rect, int shrinkWidth, int shrinkHeight)
    {
        return new Rect(rect.X + shrinkWidth, rect.Y + shrinkHeight, rect.Width - 2 * shrinkWidth, rect.Height - 2 * shrinkHeight);
    }
}
