using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 特殊相邻传送点命中判定 —— 纯函数、无 IO、无副作用（参考 KazuhaCollectSyncDecisions 模式）。
/// 输入清单 + 坐标 + 容差 + 默认缩放，输出 (是否命中, 命中时应使用的缩放)。
/// 未命中 / null / 空清单 → (false, defaultZoom)，绝不抛异常。
/// </summary>
public static class SpecialAdjacentTpPointDecisions
{
    /// <summary>
    /// 命中判定。存在某条目与 (x,y) 欧氏距离 &lt;= tolerance 即命中；
    /// 命中时返回该条目 Zoom（缺省用 defaultZoom）。多条命中取第一条（清单极小、实际不重叠）。
    /// </summary>
    public static (bool hit, double zoom) IsSpecialAdjacentPoint(
        IReadOnlyList<SpecialAdjacentTpPoint>? list,
        double x, double y,
        double tolerance,
        double defaultZoom)
    {
        if (list == null || list.Count == 0)
        {
            return (false, defaultZoom);
        }

        foreach (var entry in list)
        {
            if (entry == null) continue;
            double dx = x - entry.X;
            double dy = y - entry.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist <= tolerance)
            {
                double zoom = entry.Zoom ?? defaultZoom;
                return (true, zoom);
            }
        }
        return (false, defaultZoom);
    }
}
