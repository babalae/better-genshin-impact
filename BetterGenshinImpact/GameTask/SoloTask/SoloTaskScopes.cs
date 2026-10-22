using System;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 独立任务可以出现的运行入口。
/// </summary>
[Flags]
public enum SoloTaskScopes
{
    /// <summary>
    /// 不出现在任何入口。
    /// </summary>
    None = 0,

    /// <summary>
    /// 可由独立任务页、热键和多实例单独运行。
    /// </summary>
    Standalone = 1,

    /// <summary>
    /// 出现在任务计划和一条龙中，可被编排运行。
    /// </summary>
    Plan = 2,

    /// <summary>
    /// 两种入口都可用。
    /// </summary>
    All = Standalone | Plan
}
