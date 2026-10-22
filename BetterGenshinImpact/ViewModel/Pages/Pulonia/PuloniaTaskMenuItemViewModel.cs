using System.Collections.Generic;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// “添加任务”菜单中的一项：叶子项携带要创建的任务类型，子菜单项携带下一层菜单，分隔项只用于显示分隔线。
/// 菜单在页面建立时一次生成，之后不再变化，因此是不可变对象。
/// </summary>
public sealed class PuloniaTaskMenuItemViewModel
{
    /// <summary>
    /// 分隔项和叶子项共用的空子菜单。
    /// </summary>
    private static readonly IReadOnlyList<PuloniaTaskMenuItemViewModel> NoChildren = [];

    /// <summary>
    /// 菜单显示文字；分隔项为空字符串。
    /// </summary>
    public string Header { get; }

    /// <summary>
    /// 点击后要创建的任务类型；子菜单项和分隔项为 null。
    /// </summary>
    public string? TaskType { get; }

    /// <summary>
    /// 下一层菜单；叶子项和分隔项为空列表。
    /// </summary>
    public IReadOnlyList<PuloniaTaskMenuItemViewModel> Children { get; }

    /// <summary>
    /// 是否为分隔项。
    /// </summary>
    public bool IsSeparator { get; }

    /// <summary>
    /// 是否为可点击创建任务的叶子项。
    /// </summary>
    public bool IsLeaf => TaskType is not null;

    /// <summary>
    /// 建立菜单项，只供静态工厂使用。
    /// </summary>
    /// <param name="header">显示文字。</param>
    /// <param name="taskType">任务类型。</param>
    /// <param name="children">下一层菜单。</param>
    /// <param name="isSeparator">是否为分隔项。</param>
    private PuloniaTaskMenuItemViewModel(string header, string? taskType,
        IReadOnlyList<PuloniaTaskMenuItemViewModel> children, bool isSeparator)
    {
        Header = header;
        TaskType = taskType;
        Children = children;
        IsSeparator = isSeparator;
    }

    /// <summary>
    /// 建立点击后创建指定类型任务的叶子项。
    /// </summary>
    /// <param name="header">显示文字。</param>
    /// <param name="taskType">要创建的任务类型。</param>
    /// <returns>叶子菜单项。</returns>
    public static PuloniaTaskMenuItemViewModel Leaf(string header, string taskType)
        => new(header, taskType, NoChildren, false);

    /// <summary>
    /// 建立包含下一层菜单的子菜单项。
    /// </summary>
    /// <param name="header">显示文字。</param>
    /// <param name="children">下一层菜单，调用方传入后不应再修改。</param>
    /// <returns>子菜单项。</returns>
    public static PuloniaTaskMenuItemViewModel Submenu(string header, IReadOnlyList<PuloniaTaskMenuItemViewModel> children)
        => new(header, null, children, false);

    /// <summary>
    /// 建立分隔项。
    /// </summary>
    /// <returns>分隔项。</returns>
    public static PuloniaTaskMenuItemViewModel Separator()
        => new(string.Empty, null, NoChildren, true);

    /// <summary>
    /// 调试与无障碍读屏时显示菜单文字。
    /// </summary>
    /// <returns>菜单文字；分隔项返回“分隔线”。</returns>
    public override string ToString() => IsSeparator ? "分隔线" : Header;
}
