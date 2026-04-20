using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 秘境级联选择器数据源：一条龙任务 + 配置组 + 标准秘境（按国家分组）
/// 输出 Dictionary&lt;string, List&lt;string&gt;&gt; 供 CascadeSelector 控件使用
/// </summary>
public static class DomainCascadingItems
{
    private static Dictionary<string, List<string>>? _options;

    /// <summary>
    /// 一条龙默认任务名称列表（由 ViewModel 初始化时设置）
    /// </summary>
    public static List<string> DefaultTaskNames { get; set; } = new();

    /// <summary>
    /// 级联选项数据源（一级分类 → 二级列表），供 CascadeSelector 控件使用
    /// </summary>
    public static Dictionary<string, List<string>> Options =>
        _options ??= BuildOptions();

    /// <summary>
    /// ICascadingItem 格式数据源，供 TaskSettingsPage 的 CascadingComboBox 使用
    /// </summary>
    public static IReadOnlyList<ICascadingItem> Items => BuildCascadingItems();

    /// <summary>
    /// 重建级联数据（页面加载时调用）
    /// </summary>
    public static void Rebuild()
    {
        _options = BuildOptions();
    }

    private static Dictionary<string, List<string>> BuildOptions()
    {
        var options = new Dictionary<string, List<string>>();

        // 一条龙任务分类
        if (DefaultTaskNames.Count > 0)
        {
            options["一条龙任务"] = new List<string>(DefaultTaskNames);
        }

        // 配置组分类（自动扫描 ScriptGroup 目录）
        var scriptGroupPath = Global.Absolute(@"User\ScriptGroup");
        if (Directory.Exists(scriptGroupPath))
        {
            var scripts = Directory.GetFiles(scriptGroupPath, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n)
                .ToList();
            if (scripts.Count > 0)
            {
                options["配置组"] = scripts!;
            }
        }

        // 标准秘境分类（按国家分组，显示名称带奖励信息）
        foreach (var country in MapLazyAssets.Instance.CountryToDomains.Keys.Reverse())
        {
            var domains = MapLazyAssets.Instance.CountryToDomains[country]
                .AsEnumerable()
                .Reverse()
                .Select(d => d.Name!)
                .ToList();
            if (domains.Count > 0)
            {
                options[country] = domains;
            }
        }

        return options;
    }

    /// <summary>
    /// 构建 ICascadingItem 格式数据（兼容 TaskSettingsPage 的 CascadingComboBox）
    /// </summary>
    private static IReadOnlyList<ICascadingItem> BuildCascadingItems()
    {
        return MapLazyAssets.Instance.CountryToDomains.Keys
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
            ))
            .ToList();
    }
}
