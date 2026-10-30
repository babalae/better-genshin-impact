using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>
/// 秘境级联选择框的选项：备选秘境，以及体力计划移涌活动的秘境（只列出对应类型）。
/// “跟随培养计划”是秘境来源，由界面单选项选择，不出现在选择框中。
/// 选项复用 <see cref="DomainCascadingItems.Items"/> 的叶节点，兼容现有选中值转换器。
/// </summary>
public static class TrainingGuideDomainOptions
{
    /// <summary>选择框类型：备选秘境，列出全部秘境。</summary>
    public const string FallbackPicker = "fallback";

    /// <summary>选择框类型：盛材移涌秘境，只列出精通秘境与炼武秘境。</summary>
    public const string MaterialOverflowPicker = "material_overflow";

    /// <summary>选择框类型：祝圣移涌秘境，只列出祝圣秘境。</summary>
    public const string BlessingOverflowPicker = "blessing_overflow";

    /// <summary>
    /// “跟随培养计划”的显示文字，用于体力计划的执行记录。
    /// 旧版本选择框曾以它作为选项值，读取移涌活动秘境时仍按跟随培养计划处理。
    /// </summary>
    public const string FollowTrainingPlanOption = "跟随培养计划";

    /// <summary>盛材移涌秘境的选项缓存。</summary>
    private static IReadOnlyList<ICascadingItem>? _materialOverflowItems;

    /// <summary>祝圣移涌秘境的选项缓存。</summary>
    private static IReadOnlyList<ICascadingItem>? _blessingOverflowItems;

    /// <summary>备选秘境：按国家分组的全部秘境。</summary>
    public static IReadOnlyList<ICascadingItem> Items => DomainCascadingItems.Items;

    /// <summary>盛材移涌秘境：按国家分组的精通秘境与炼武秘境。</summary>
    public static IReadOnlyList<ICascadingItem> MaterialOverflowItems =>
        _materialOverflowItems ??= BuildTypedItems("MasteryDomain", "ForgeryDomain");

    /// <summary>祝圣移涌秘境：按国家分组的祝圣秘境。</summary>
    public static IReadOnlyList<ICascadingItem> BlessingOverflowItems =>
        _blessingOverflowItems ??= BuildTypedItems("BlessDomain");

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

    /// <summary>选择框类型是否带“秘境来源”（跟随培养计划 / 指定秘境）：移涌活动秘境为空时跟随培养计划。</summary>
    /// <param name="picker">选择框类型。</param>
    /// <returns>移涌活动秘境返回 true；备选秘境返回 false。</returns>
    public static bool HasTrainingPlanSource(string? picker) => picker is MaterialOverflowPicker or BlessingOverflowPicker;

    /// <summary>在选项中查找参数值对应的叶节点。</summary>
    /// <param name="items">选择框的选项。</param>
    /// <param name="name">参数中保存的秘境名。</param>
    /// <returns>叶节点；空值或没有对应选项时为 null。</returns>
    public static ICascadingItem? Find(IReadOnlyList<ICascadingItem> items, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var group in items)
        {
            var found = group.Children?.FirstOrDefault(item => item.Tag as string == name);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>建立只含指定类型秘境的选项：国家组只保留对应类型的秘境，空组不显示。</summary>
    /// <param name="types">保留的秘境类型（tp.json 的 type）。</param>
    /// <returns>级联选项。</returns>
    private static IReadOnlyList<ICascadingItem> BuildTypedItems(params string[] types)
    {
        var domains = MapLazyAssets.Get().DomainPositionMap;
        var result = new List<ICascadingItem>();
        foreach (var country in DomainCascadingItems.Items)
        {
            if (country.Children == null)
                continue;

            var filtered = country.Children.Where(item => item.Tag is string name
                && domains.TryGetValue(name, out var position) && position.Type is { } type && types.Contains(type)).ToArray();
            if (filtered.Length > 0)
                result.Add(new CascadingItem(country.Label, filtered) { Tag = country.Tag });
        }

        return result;
    }
}
