using System;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 声明一个可被发现的独立任务。注册表在启动时扫描带此特性的类。
/// </summary>
/// <param name="id">稳定标识，小写 snake_case，发布后不改。</param>
/// <param name="displayName">面向用户的显示名。</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SoloTaskInfoAttribute(string id, string displayName) : Attribute
{
    /// <summary>
    /// 稳定标识，独立任务页、多实例、任务计划都用它引用任务。
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// 面向用户的显示名。
    /// </summary>
    public string DisplayName { get; } = displayName;

    /// <summary>
    /// 用途说明。
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 菜单中的分组名，同组任务排在一起。
    /// </summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>
    /// 菜单排序，数值小的在前。约定按百位区分分组：日常奖励 1xx、树脂与战斗 2xx、采集与生活 3xx、工具 4xx、小游戏 5xx、调试 6xx。
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// 任务可以出现的运行入口，默认两种入口都可用。
    /// </summary>
    public SoloTaskScopes Scopes { get; init; } = SoloTaskScopes.All;

    /// <summary>
    /// 新启动截图器时是否先等待进入主界面。
    /// </summary>
    public bool WaitForMainUi { get; init; } = true;

    /// <summary>
    /// 大于 0 表示新建一条龙配置时默认加入，并按此值排序。
    /// </summary>
    public int OneDragonOrder { get; init; }
}
