using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Model.MaskMap;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 统计标点分类网格中每个分类的已标记点位数量，用于显示“已标记/总数”（与 HoYoLAB 地图一致）。
/// </summary>
public static class MapLabelMarkedCount
{
    /// <summary>
    /// 按点位的 LabelId 归类并设置每个分类项的已标记数量
    /// </summary>
    /// <param name="items">网格中的分类项</param>
    /// <param name="points">分类下的全部点位</param>
    /// <param name="isMarked">判断点位是否已标记</param>
    public static void Apply(IEnumerable<MapLabelItemVm> items, IEnumerable<MaskMapPoint> points, Func<MaskMapPoint, bool> isMarked)
    {
        // 每个 LabelId 下的已标记数量
        var markedByLabel = points
            .Where(isMarked)
            .GroupBy(p => p.LabelId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        // 合并分类（多个 LabelIds）累加各子分类
        foreach (var item in items)
        {
            item.MarkedCount = item.LabelIds.Sum(id => markedByLabel.GetValueOrDefault(id));
        }
    }
}
