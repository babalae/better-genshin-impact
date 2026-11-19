using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 同步标点分类网格中的“已选中”状态。
/// 已选列表与网格中的实例可能不同（从存储恢复、切换分类后重新创建），因此按 ID 匹配。
/// </summary>
public static class MapLabelSelection
{
    /// <summary>
    /// 按已选列表设置每个网格项的选中状态
    /// </summary>
    /// <param name="items">网格中的分类项</param>
    /// <param name="selected">已选中的分类项</param>
    public static void Apply(IEnumerable<MapLabelItemVm> items, IEnumerable<MapLabelItemVm> selected)
    {
        var selectedIds = selected.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in items)
        {
            item.IsSelected = selectedIds.Contains(item.Id);
        }
    }
}
