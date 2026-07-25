using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Helpers;

public static class DomainCascadingItems
{
    private static IReadOnlyList<ICascadingItem>? _items;

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
        ["「浪迹」的哲学"] = (3, "「浪迹」(3/6)")
    };

    private static IReadOnlyList<ICascadingItem> BuildItems()
    {
        return MapLazyAssets.Get().CountryToDomains.Keys
            .Reverse()
            .Select(country => (ICascadingItem)new CascadingItem(
                country,
                MapLazyAssets.Get().CountryToDomains[country]
                    .AsEnumerable()
                    .Reverse()
                    .Select(d => (ICascadingItem)new CascadingItem(
                        d.Name! + " | " + FormatRewards(d.Rewards))
                    {
                        Tag = d.Name
                    })
            ))
            .ToList();
    }

    private static string FormatRewards(IEnumerable<string> rewards)
    {
        return string.Join(" ", rewards
            .Select((reward, index) => TalentRewardDisplays.TryGetValue(reward, out var display)
                ? (display.Order, Index: index, display.Text)
                : (Order: int.MaxValue, Index: index, Text: reward))
            .OrderBy(reward => reward.Order)
            .ThenBy(reward => reward.Index)
            .Select(reward => reward.Text));
    }
}
