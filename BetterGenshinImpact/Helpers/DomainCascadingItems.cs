using BetterGenshinImpact.Service.I18n;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 秘境级联选择框的选项：第一级为国家，第二级为该国家的秘境（显示“秘境名 | 奖励”，Tag 为秘境名）。
/// 只列出真实的秘境；“跟随培养计划”属于秘境来源，由界面的单选项单独选择，不放在这里。
/// </summary>
public static class DomainCascadingItems
{
    private static IReadOnlyList<ICascadingItem>? _items;

    /// <summary>
    /// 登记国家名 i18n Key。国家名来自地图资源，同步工具无法静态扫描，在此用 I18nService.Key 显式登记；
    /// 新增国家时需同步补充。
    /// </summary>
    private static readonly IReadOnlyList<string> CountryNameKeys =
    [
        I18nService.Key("蒙德"), I18nService.Key("璃月"), I18nService.Key("稻妻"), I18nService.Key("须弥"),
        I18nService.Key("枫丹"), I18nService.Key("纳塔"), I18nService.Key("挪德卡莱"), I18nService.Key("至冬"),
    ];

    public static IReadOnlyList<ICascadingItem> Items =>
        _items ??= BuildItems();

    private static readonly Dictionary<string, (int Order, string Text)> TalentRewardDisplays = new()
    {
        ["「自由」的哲学"] = (1, "「自由」(1/4)"),
        ["「抗争」的哲学"] = (2, "「抗争」(2/5)"),
        ["「诗文」的哲学"] = (3, "「诗文」(3/6)"),
        ["「繁荣」的哲学"] = (1, "「繁荣」(1/4)"),
        ["「勤劳」的哲学"] = (2, "「勤劳」(2/5)"),
        ["「黄金」的哲学"] = (3, "「黄金」(3/6)"),
        ["「浮世」的哲学"] = (1, "「浮世」(1/4)"),
        ["「风雅」的哲学"] = (2, "「风雅」(2/5)"),
        ["「天光」的哲学"] = (3, "「天光」(3/6)"),
        ["「诤言」的哲学"] = (1, "「诤言」(1/4)"),
        ["「巧思」的哲学"] = (2, "「巧思」(2/5)"),
        ["「笃行」的哲学"] = (3, "「笃行」(3/6)"),
        ["「公平」的哲学"] = (1, "「公平」(1/4)"),
        ["「正义」的哲学"] = (2, "「正义」(2/5)"),
        ["「秩序」的哲学"] = (3, "「秩序」(3/6)"),
        ["「角逐」的哲学"] = (1, "「角逐」(1/4)"),
        ["「焚燔」的哲学"] = (2, "「焚燔」(2/5)"),
        ["「纷争」的哲学"] = (3, "「纷争」(3/6)"),
        ["「月光」的哲学"] = (1, "「月光」(1/4)"),
        ["「乐园」的哲学"] = (2, "「乐园」(2/5)"),
        ["「浪迹」的哲学"] = (3, "「浪迹」(3/6)"),
        ["「慈爱」的哲学"] = (1, "「慈爱」(1/4)"),
        ["「坚忍」的哲学"] = (2, "「坚忍」(2/5)"),
        ["「荣光」的哲学"] = (3, "「荣光」(3/6)")
    };

    private static IReadOnlyList<ICascadingItem> BuildItems()
    {
        var items = MapLazyAssets.Get().CountryToDomains.Keys
            .Reverse()
            .Select(country => (ICascadingItem)new CascadingItem(
                // 国家只作为分组标签展示，可直接翻译；保存值为二级项 Tag 中的秘境名。
                I18nService.Instance.Translate(country),
                MapLazyAssets.Get().CountryToDomains[country]
                    .AsEnumerable()
                    .Reverse()
                    .Select(d => (ICascadingItem)new CascadingItem(
                        // 展示文本按界面语言翻译；Tag 保持中文秘境名，作为保存值。
                        I18nService.Instance.Translate(d.Name!) + " | " + FormatRewards(d.Rewards))
                    {
                        Tag = d.Name
                    })
            ))
            .ToList();
        return items;
    }

    private static string FormatRewards(IEnumerable<string> rewards)
    {
        // 中文沿用空格分隔；其他语言的奖励名含空格，改用翻译后的顿号（如英文“, ”）分隔。
        var separator = I18nService.Instance.Translate("、");
        if (separator == "、")
        {
            separator = " ";
        }

        return string.Join(separator, rewards
            .Select((reward, index) => TalentRewardDisplays.TryGetValue(reward, out var display)
                ? (display.Order, Index: index, display.Text)
                : (Order: int.MaxValue, Index: index, Text: reward))
            .OrderBy(reward => reward.Order)
            .ThenBy(reward => reward.Index)
            .Select(reward => I18nService.Instance.Translate(reward.Text)));
    }
}
