namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 任务结果种类。取消不属于结果，以 OperationCanceledException 表示。
/// </summary>
public enum SoloTaskResultKind
{
    /// <summary>
    /// 完成，且完成情况已确认。
    /// </summary>
    Succeeded,

    /// <summary>
    /// 流程正常结束，但没有确认副作用。
    /// </summary>
    Unverified,

    /// <summary>
    /// 只确认了部分副作用。
    /// </summary>
    Partial,

    /// <summary>
    /// 业务失败，必须带原因码。
    /// </summary>
    Failed,

    /// <summary>
    /// 条件不满足，没有执行。
    /// </summary>
    Skipped
}
