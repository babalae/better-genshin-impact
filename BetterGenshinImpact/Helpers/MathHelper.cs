using OpenCvSharp;
using System;

namespace BetterGenshinImpact.Helpers;

public class MathHelper
{
    /// <summary>
    /// 点到直线的最短距离
    /// </summary>
    /// <param name="point"></param>
    /// <param name="point1"></param>
    /// <param name="point2"></param>
    /// <returns></returns>
    public static double Distance(Point point, Point point1, Point point2)
    {
        // 直线的方向向量
        double a = point2.Y - point1.Y;
        double b = point1.X - point2.X;
        double c = point2.X * point1.Y - point1.X * point2.Y;

        // 使用距离公式计算点到直线的最短距离
        double numerator = Math.Abs(a * point.X + b * point.Y + c);
        double denominator = Math.Sqrt(a * a + b * b);
        double distance = numerator / denominator;

        return distance;
    }

    /// <summary>
    /// 两点之间的距离
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static double Distance(Point p1, Point p2)
    {
        return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }

    public static double Distance(Point2f p1, Point2f p2)
    {
        return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }
}
