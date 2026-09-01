using System;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

public static class BigMapPriorMatchDecisions
{
    public const double Layer1RangeGenshin = 100.0; // 第一层小地图先验范围
    public const double Layer2RangeGenshin = 500.0; // 第二层目标坐标先验范围
    public const double RegionCenterRangeGenshin = 200.0; // 区域中心点先验范围（切换区域后，中心点已知精确，可用较大半径）

    /// <summary>
    /// 判断某层区块限定匹配结果是否可信：非空 且 距该层先验中心 ≤ 该层范围。
    /// </summary>
    public static bool IsResultAcceptable(bool isEmpty, Point2f resultGenshin, Point2f centerGenshin, double rangeGenshin)
    {
        if (isEmpty) return false;
        var dist = Math.Sqrt(Math.Pow(resultGenshin.X - centerGenshin.X, 2)
                           + Math.Pow(resultGenshin.Y - centerGenshin.Y, 2));
        return dist <= rangeGenshin;
    }

    /// <summary>距先验中心的距离（原神坐标），供日志打印。</summary>
    public static double Distance(Point2f a, Point2f b)
        => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
