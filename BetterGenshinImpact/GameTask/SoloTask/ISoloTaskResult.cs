namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 按任务标识动态调用时使用的无类型结果视图。
/// </summary>
public interface ISoloTaskResult
{
    /// <summary>
    /// 结果种类。
    /// </summary>
    SoloTaskResultKind Kind { get; }

    /// <summary>
    /// 面向用户的原因或摘要。
    /// </summary>
    string Message { get; }

    /// <summary>
    /// 机器可读的原因码，成功时通常为空。
    /// </summary>
    string? ReasonCode { get; }

    /// <summary>
    /// 附加数据；没有时为 null 或 <see cref="NoData"/>。
    /// </summary>
    object? Data { get; }
}
