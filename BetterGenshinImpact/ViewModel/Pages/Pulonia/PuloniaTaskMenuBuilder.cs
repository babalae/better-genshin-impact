using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 由已注册的能力定义生成“添加任务”菜单。一条龙页使用完整菜单，任务计划页只使用其中的“独立任务”一层。
/// </summary>
public static class PuloniaTaskMenuBuilder
{
    /// <summary>
    /// 独立任务子菜单的标题。
    /// </summary>
    public const string SoloTaskMenuHeader = "独立任务";

    /// <summary>
    /// 资源类与通用任务类型的固定顺序和菜单文字；带省略号表示点击后会打开创建对话框。
    /// </summary>
    private static readonly (string TaskType, string Header)[] KnownTaskTypes =
    [
        ("pathing", "地图追踪任务…"),
        ("javascript", "JS 脚本任务…"),
        ("keymouse", "录制回放任务…"),
        ("shell", "Shell 任务…"),
        ("csharp", "进程内 C# 任务…")
    ];

    /// <summary>
    /// 生成完整的添加任务菜单：先放“独立任务”子菜单，再放资源类与其他非独立任务。
    /// </summary>
    /// <param name="definitions">已注册的全部能力定义。</param>
    /// <returns>菜单第一层的项目。</returns>
    public static IReadOnlyList<PuloniaTaskMenuItemViewModel> Build(IEnumerable<PuloniaTaskDefinition> definitions)
    {
        var all = definitions.ToList();
        var menu = new List<PuloniaTaskMenuItemViewModel>();

        // 1. 独立任务收拢到一个子菜单；一个独立任务都没有时不显示空子菜单
        var soloMenu = BuildSoloTaskMenu(all);
        if (soloMenu.Count > 0)
        {
            menu.Add(PuloniaTaskMenuItemViewModel.Submenu(SoloTaskMenuHeader, soloMenu));
        }

        // 2. 非独立任务按任务类型去重：已知类型按固定顺序和固定文字，未知类型排在后面并使用显示名
        var others = all
            .Where(definition => !definition.IsSoloTask && !string.IsNullOrWhiteSpace(definition.TaskType))
            .GroupBy(definition => definition.TaskType, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        foreach (var (taskType, header) in KnownTaskTypes)
        {
            if (others.Any(definition => definition.TaskType == taskType))
            {
                menu.Add(PuloniaTaskMenuItemViewModel.Leaf(header, taskType));
            }
        }

        foreach (var definition in others.Where(definition => KnownTaskTypes.All(known => known.TaskType != definition.TaskType)))
        {
            menu.Add(PuloniaTaskMenuItemViewModel.Leaf(DisplayNameOf(definition), definition.TaskType));
        }

        return menu.AsReadOnly();
    }

    /// <summary>
    /// 只生成“独立任务”子菜单里的内容：独立任务按 Order 排序，Group 变化处插入分隔项。
    /// </summary>
    /// <param name="definitions">已注册的全部能力定义，非独立任务会被忽略。</param>
    /// <returns>子菜单项目；没有独立任务时为空列表。</returns>
    public static IReadOnlyList<PuloniaTaskMenuItemViewModel> BuildSoloTaskMenu(IEnumerable<PuloniaTaskDefinition> definitions)
    {
        // 注册表已按 Order 排列，这里再排序一次，保证调用方传入任意顺序时结果稳定
        var solo = definitions
            .Where(definition => definition.IsSoloTask && !string.IsNullOrWhiteSpace(definition.TaskType))
            .GroupBy(definition => definition.TaskType, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(definition => definition.Order)
            .ThenBy(definition => definition.TaskType, StringComparer.Ordinal)
            .ToList();

        var menu = new List<PuloniaTaskMenuItemViewModel>();
        string? previousGroup = null;
        foreach (var definition in solo)
        {
            // 分组变化处插入分隔线；第一项前不插
            if (menu.Count > 0 && !string.Equals(previousGroup, definition.Group, StringComparison.Ordinal))
            {
                menu.Add(PuloniaTaskMenuItemViewModel.Separator());
            }

            menu.Add(PuloniaTaskMenuItemViewModel.Leaf(DisplayNameOf(definition), definition.TaskType));
            previousGroup = definition.Group;
        }

        return menu.AsReadOnly();
    }

    /// <summary>
    /// 取菜单显示名；能力没有显示名时退回任务类型。
    /// </summary>
    /// <param name="definition">能力定义。</param>
    /// <returns>显示名。</returns>
    private static string DisplayNameOf(PuloniaTaskDefinition definition)
        => string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.TaskType : definition.DisplayName;
}
