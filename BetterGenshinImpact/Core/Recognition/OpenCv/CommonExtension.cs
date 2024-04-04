using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using Vanara.PInvoke;
using Color = System.Windows.Media.Color;
using Point = OpenCvSharp.Point;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

public static class CommonExtension
{
    public static unsafe Point ToCvPoint(this System.Drawing.Point point)
    {
        return *(Point*)&point;
    }

    public static unsafe System.Drawing.Point ToDrawingPoint(this Point point)
    {
        return *(System.Drawing.Point*)&point;
    }

    public static System.Windows.Point ToWindowsPoint(this Point point)
    {
        return new System.Windows.Point(point.X, point.Y);
    }

    public static unsafe Rect ToCvRect(this Rectangle rectangle)
    {
        return *(Rect*)&rectangle;
    }

    public static System.Windows.Rect ToWindowsRectangle(this Rect rect)
    {
        return new System.Windows.Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static System.Windows.Rect ToWindowsRectangleOffset(this Rect rect, int offsetX, int offsetY)
    {
        return new System.Windows.Rect(rect.X + offsetX, rect.Y + offsetY, rect.Width, rect.Height);
    }

    public static unsafe Rectangle ToDrawingRectangle(this Rect rect)
    {
        return *(Rectangle*)&rect;
    }

    public static System.Drawing.Point GetCenterPoint(this Rectangle rectangle)
    {
        if (rectangle.IsEmpty) throw new ArgumentException("rectangle is empty");

        return new System.Drawing.Point(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);
    }

    public static Point GetCenterPoint(this RECT rectangle)
    {
        if (rectangle.IsEmpty) throw new ArgumentException("rectangle is empty");

        return new Point(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);
    }

    public static Point GetCenterPoint(this Rect rectangle)
    {
        if (rectangle == Rect.Empty) throw new ArgumentException("rectangle is empty");

        return new Point(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);
    }

    public static Rect Multiply(this Rect rect, double assetScale)
    {
        if (rect == Rect.Empty) throw new ArgumentException("rect is empty");

        return new Rect((int)(rect.X * assetScale), (int)(rect.Y * assetScale), (int)(rect.Width * assetScale), (int)(rect.Height * assetScale));
    }

    public static Color ToWindowsColor(this System.Drawing.Color color)
    {
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    public static Point2d ToPoint2d(this Point2f p)
    {
        return new Point2d(p.X, p.Y);
    }

    public static List<Point2d> ToPoint2d(this List<Point2f> list)
    {
        return list.ConvertAll(ToPoint2d);
    }
}
