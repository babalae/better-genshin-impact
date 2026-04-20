using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Helpers;

public static class DomainCascadingItems
{
    private static IReadOnlyList<ICascadingItem>? _items;

    public static IReadOnlyList<ICascadingItem> Items =>
        _items ??= BuildItems(null);

    /// <summary>
    /// 重建级联数据，支持动态自定义分类
    /// </summary>
    public static void Rebuild(List<string>? customDomainList)
    {
        _items = BuildItems(customDomainList);
    }

    private static IReadOnlyList<ICascadingItem> BuildItems(List<string>? customDomainList)
    {
        var items = new List<ICascadingItem>();

        // 自定义配置组分类（仅在列表非空时显示）
        if (customDomainList != null && customDomainList.Count > 0)
        {
            items.Add(new CascadingItem("自定义",
                customDomainList.Select(name =>
                    (ICascadingItem)new CascadingItem(name) { Tag = name })));
        }

        // 标准秘境分类（按国家分组）
        items.AddRange(MapLazyAssets.Instance.CountryToDomains.Keys
            .Reverse()
            .Select(country => (ICascadingItem)new CascadingItem(
                country,
                MapLazyAssets.Instance.CountryToDomains[country]
                    .AsEnumerable()
                    .Reverse()
                    .Select(d => (ICascadingItem)new CascadingItem(
                        d.Name! + " | " + string.Join(" ", d.Rewards))
                    {
                        Tag = d.Name
                    })
            )));

        return items;
    }
}
