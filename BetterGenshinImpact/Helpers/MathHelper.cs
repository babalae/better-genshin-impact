using System;
using OpenCvSharp;

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
    public static double CalculateDistance(Point point, Point point1, Point point2)
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
}
