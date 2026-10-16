using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>备选秘境直接派生自正式选项，不维护第二份秘境列表。</summary>
public static class TrainingGuideDomainOptions
{
    private static IReadOnlyList<ICascadingItem>? _items;

    public static IReadOnlyList<ICascadingItem> Items =>
        _items ??= Filter(DomainCascadingItems.Items);

    public static IReadOnlyList<string> RewardSelectionIndices { get; } = ["", "1", "2", "3"];

    private static IReadOnlyList<ICascadingItem> Filter(IEnumerable<ICascadingItem> items)
    {
        var result = new List<ICascadingItem>();
        foreach (var item in items)
        {
            if (item.Label != "自动选择" || item.Children == null)
            {
                result.Add(item);
                continue;
            }

            var children = item.Children.ToArray();
            var filtered = children.Where(child =>
                (child.Tag as string ?? child.Label) != AutoDomainTask.TrainingGuideOption).ToArray();
            if (filtered.Length == children.Length)
                result.Add(item);
            else if (filtered.Length > 0)
                result.Add(new CascadingItem(item.Label, filtered) { Tag = item.Tag });
            // 仅重建发生过滤的父节点；不修改共享列表，保留叶节点以兼容现有选中值转换器。
        }
        return result;
    }
}
