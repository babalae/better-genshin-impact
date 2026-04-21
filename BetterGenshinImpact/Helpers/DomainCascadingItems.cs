using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 秘境级联选择器数据源：一条龙任务 + 配置组 + 标准秘境（按国家分组）
/// 
/// 存储格式约定：DomainName 使用 "type:name" 前缀区分来源
///   - "task:领取邮件"   → 一条龙默认任务
///   - "script:我的配置" → 配置组（ScriptGroup）
///   - "忘却之峡"        → 标准秘境（无前缀，向后兼容）
/// </summary>
public static class DomainCascadingItems
{
    /// <summary>类型前缀：一条龙默认任务</summary>
    public const string TaskPrefix = "task:";
    /// <summary>类型前缀：配置组</summary>
    public const string ScriptPrefix = "script:";

    private static Dictionary<string, List<string>>? _options;

    /// <summary>
    /// 一条龙默认任务名称列表（由 ViewModel 初始化时设置，不含"自动秘境"自身）
    /// </summary>
    public static List<string> DefaultTaskNames { get; set; } = new();

    /// <summary>
    /// 级联选项数据源（一级分类 → 二级列表），值带类型前缀
    /// </summary>
    public static Dictionary<string, List<string>> Options =>
        _options ??= BuildOptions();

    /// <summary>
    /// ICascadingItem 格式数据源，供 TaskSettingsPage 的 CascadingComboBox 使用（仅标准秘境）
    /// </summary>
    public static IReadOnlyList<ICascadingItem> Items => BuildCascadingItems();

    /// <summary>
    /// 重建级联数据（页面加载时调用）
    /// </summary>
    public static void Rebuild()
    {
        _options = BuildOptions();
    }

    /// <summary>
    /// 从带前缀的 DomainName 中解析类型和名称
    /// </summary>
    public static (string type, string name) Parse(string? domainName)
    {
        if (string.IsNullOrEmpty(domainName))
            return ("", "");
        if (domainName.StartsWith(TaskPrefix))
            return ("task", domainName[TaskPrefix.Length..]);
        if (domainName.StartsWith(ScriptPrefix))
            return ("script", domainName[ScriptPrefix.Length..]);
        return ("domain", domainName); // 标准秘境无前缀
    }

    private static Dictionary<string, List<string>> BuildOptions()
    {
        var options = new Dictionary<string, List<string>>();

        // 一条龙任务分类（带 task: 前缀）
        if (DefaultTaskNames.Count > 0)
        {
            options["一条龙任务"] = DefaultTaskNames
                .Select(n => TaskPrefix + n)
                .ToList();
        }

        // 配置组分类（带 script: 前缀，自动扫描 ScriptGroup 目录）
        var scriptGroupPath = Global.Absolute(@"User\ScriptGroup");
        if (Directory.Exists(scriptGroupPath))
        {
            var scripts = Directory.GetFiles(scriptGroupPath, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n)
                .Select(n => ScriptPrefix + n)
                .ToList();
            if (scripts.Count > 0)
            {
                options["配置组"] = scripts;
            }
        }

        // 标准秘境分类（无前缀，向后兼容）
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
    /// 构建 ICascadingItem 格式数据（兼容 TaskSettingsPage 的 CascadingComboBox，仅标准秘境）
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
