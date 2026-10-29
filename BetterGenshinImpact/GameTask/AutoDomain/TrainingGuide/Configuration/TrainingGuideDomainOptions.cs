using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>
/// 秘境级联选择框的选项：备选秘境，以及体力计划移涌活动的秘境（只列出对应类型，首项为“跟随培养计划”）。
/// 选项复用 <see cref="DomainCascadingItems.Items"/> 的叶节点，兼容现有选中值转换器。
/// </summary>
public static class TrainingGuideDomainOptions
{
    /// <summary>选择框类型：备选秘境，不提供“根据提升指南选择”。</summary>
    public const string FallbackPicker = "fallback";

    /// <summary>选择框类型：盛材移涌秘境，只列出精通秘境与炼武秘境。</summary>
    public const string MaterialOverflowPicker = "material_overflow";

    /// <summary>选择框类型：祝圣移涌秘境，只列出祝圣秘境。</summary>
    public const string BlessingOverflowPicker = "blessing_overflow";

    /// <summary>“跟随培养计划”选项的显示值；选中后参数保存为空字符串。</summary>
    public const string FollowTrainingPlanOption = "跟随培养计划";

    private static IReadOnlyList<ICascadingItem>? _items;

    /// <summary>盛材移涌秘境的选项缓存。</summary>
    private static IReadOnlyList<ICascadingItem>? _materialOverflowItems;

    /// <summary>祝圣移涌秘境的选项缓存。</summary>
    private static IReadOnlyList<ICascadingItem>? _blessingOverflowItems;

    public static IReadOnlyList<ICascadingItem> Items =>
        _items ??= Filter(DomainCascadingItems.Items);

    /// <summary>盛材移涌秘境：首项“跟随培养计划”，其余为按国家分组的精通秘境与炼武秘境。</summary>
    public static IReadOnlyList<ICascadingItem> MaterialOverflowItems =>
        _materialOverflowItems ??= BuildOverflowItems("跟随培养计划（按提升指南选择秘境）", "MasteryDomain", "ForgeryDomain");

    /// <summary>祝圣移涌秘境：首项“跟随培养计划”，其余为按国家分组的祝圣秘境。</summary>
    public static IReadOnlyList<ICascadingItem> BlessingOverflowItems =>
        _blessingOverflowItems ??= BuildOverflowItems("跟随培养计划（养成角色推荐的圣遗物）", "BlessDomain");

    public static IReadOnlyList<string> RewardSelectionIndices { get; } = ["", "1", "2", "3"];

    /// <summary>按选择框类型返回选项；未知类型按备选秘境处理。</summary>
    /// <param name="picker">选择框类型。</param>
    /// <returns>级联选项。</returns>
    public static IReadOnlyList<ICascadingItem> ForPicker(string? picker) => picker switch
    {
        MaterialOverflowPicker => MaterialOverflowItems,
        BlessingOverflowPicker => BlessingOverflowItems,
        _ => Items
    };

    /// <summary>
    /// 在选项中查找参数值对应的叶节点：移涌活动秘境的空值对应“跟随培养计划”，其余按秘境名匹配。
    /// </summary>
    /// <param name="items">选择框的选项。</param>
    /// <param name="name">参数中保存的秘境名。</param>
    /// <returns>叶节点；没有对应选项时为 null。</returns>
    public static ICascadingItem? Find(IReadOnlyList<ICascadingItem> items, string? name)
    {
        var tag = string.IsNullOrWhiteSpace(name) ? FollowTrainingPlanOption : name;
        foreach (var group in items)
        {
            var found = group.Children?.FirstOrDefault(item => item.Tag as string == tag);
            if (found != null)
                return found;
        }

        return null;
    }

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

    /// <summary>
    /// 建立移涌活动秘境的选项：首组为“跟随培养计划”，其余国家组只保留指定类型的秘境，空组不显示。
    /// </summary>
    /// <param name="followLabel">“跟随培养计划”选项展开时显示的说明。</param>
    /// <param name="types">保留的秘境类型（tp.json 的 type）。</param>
    /// <returns>级联选项。</returns>
    private static IReadOnlyList<ICascadingItem> BuildOverflowItems(string followLabel, params string[] types)
    {
        var domains = MapLazyAssets.Get().DomainPositionMap;
        var result = new List<ICascadingItem>
        {
            new CascadingItem("自动选择", new ICascadingItem[]
            {
                new CascadingItem(followLabel) { Tag = FollowTrainingPlanOption }
            })
        };
        foreach (var country in DomainCascadingItems.Items)
        {
            if (country.Label == "自动选择" || country.Children == null)
                continue;

            var filtered = country.Children.Where(item => item.Tag is string name
                && domains.TryGetValue(name, out var position) && position.Type is { } type && types.Contains(type)).ToArray();
            if (filtered.Length > 0)
                result.Add(new CascadingItem(country.Label, filtered) { Tag = country.Tag });
        }

        return result;
    }
}
