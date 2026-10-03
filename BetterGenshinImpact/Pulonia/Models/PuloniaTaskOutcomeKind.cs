namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 任务动作的业务完成程度；执行结束不等于游戏副作用已经确认。
/// </summary>
public enum PuloniaTaskOutcomeKind
{
    /// <summary>
    /// 动作及其声明的完成合同已经确认。
    /// </summary>
    Succeeded,

    /// <summary>
    /// 执行器已经正常结束，但没有足够证据确认游戏副作用。
    /// </summary>
    ExecutedUnverified,

    /// <summary>
    /// 只确认了部分副作用，剩余工作仍未完成。
    /// </summary>
    PartiallySucceeded,

    /// <summary>
    /// 动作执行失败。
    /// </summary>
    Failed,

    /// <summary>
    /// 动作被取消。
    /// </summary>
    Cancelled,

    /// <summary>
    /// 动作因条件或额度限制被跳过。
    /// </summary>
    Skipped,

    /// <summary>
    /// 动作可能已经产生副作用，需要人工或能力核验。
    /// </summary>
    NeedsAttention
}
