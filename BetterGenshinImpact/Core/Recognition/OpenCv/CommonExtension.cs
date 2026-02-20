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
    public static unsafe Point AsCvPoint(this System.Drawing.Point point)
    {
        return *(Point*)&point;
    }

    public static unsafe System.Drawing.Point AsDrawingPoint(this Point point)
    {
        return *(System.Drawing.Point*)&point;
    }

    public static System.Windows.Point ToWindowsPoint(this Point point)
    {
        return new System.Windows.Point(point.X, point.Y);
    }

    public static unsafe Rect AsCvRect(this Rectangle rectangle)
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

    public static unsafe Rectangle AsDrawingRectangle(this Rect rect)
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
        if (rectangle == default) throw new ArgumentException("rectangle is empty");

        return new Point(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);
    }

    public static Rect Multiply(this Rect rect, double assetScale)
    {
        if (rect == default) throw new ArgumentException("rect is empty");

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

    /// <summary>
    /// 将矩形钳位到指定尺寸范围内（交集语义），防止 OpenCV ROI 越界
    /// </summary>
    public static Rect ClampTo(this Rect rect, int maxWidth, int maxHeight)
    {
        int x1 = Math.Clamp(rect.X, 0, maxWidth);
        int y1 = Math.Clamp(rect.Y, 0, maxHeight);
        int x2 = Math.Clamp(rect.X + rect.Width, 0, maxWidth);
        int y2 = Math.Clamp(rect.Y + rect.Height, 0, maxHeight);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// 将矩形钳位到 Mat 范围内（交集语义），防止 OpenCV ROI 越界
    /// </summary>
    public static Rect ClampTo(this Rect rect, Mat mat)
    {
        return rect.ClampTo(mat.Cols, mat.Rows);
    }
}
