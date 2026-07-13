using OpenCvSharp;
using System;

namespace BetterGenshinImpact.Helpers.Extensions;

public static class PointExtension
{
    // Point2f is empty
    public static bool IsEmpty(this Point2f point) => point is { X: 0, Y: 0 };

    // build Rect from Point2f
    public static Rect CenterToRect(this Point2f point, int width, int height) =>
        new((int)Math.Round(point.X - width / 2f, 0), (int)Math.Round(point.Y - height / 2f, 0), width, height);
}
