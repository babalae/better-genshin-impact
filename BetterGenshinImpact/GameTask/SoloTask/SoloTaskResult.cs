namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 不带数据的任务结果，以及创建结果的工厂方法。可隐式转换为任意 <see cref="SoloTaskResult{TData}"/>。
/// </summary>
/// <param name="Kind">结果种类。</param>
/// <param name="Message">面向用户的原因或摘要。</param>
/// <param name="ReasonCode">机器可读的原因码。</param>
public sealed record SoloTaskResult(SoloTaskResultKind Kind, string Message, string? ReasonCode)
{
    /// <summary>
    /// 成功且已确认。
    /// </summary>
    /// <param name="message">结果摘要。</param>
    public static SoloTaskResult Success(string message = "") => new(SoloTaskResultKind.Succeeded, message, null);

    /// <summary>
    /// 成功并附带数据。
    /// </summary>
    /// <param name="data">附加数据。</param>
    /// <param name="message">结果摘要。</param>
    public static SoloTaskResult<T> Success<T>(T data, string message = "") where T : class
        => new(SoloTaskResultKind.Succeeded, message, null, data);

    /// <summary>
    /// 流程正常结束，副作用未确认。
    /// </summary>
    /// <param name="message">结果摘要。</param>
    public static SoloTaskResult Unverified(string message = "") => new(SoloTaskResultKind.Unverified, message, null);

    /// <summary>
    /// 流程正常结束，副作用未确认，并附带数据。
    /// </summary>
    /// <param name="data">附加数据。</param>
    /// <param name="message">结果摘要。</param>
    public static SoloTaskResult<T> Unverified<T>(T data, string message = "") where T : class
        => new(SoloTaskResultKind.Unverified, message, null, data);

    /// <summary>
    /// 只确认了部分副作用。
    /// </summary>
    /// <param name="reasonCode">未完成部分的原因码。</param>
    /// <param name="message">结果摘要。</param>
    public static SoloTaskResult Partial(string reasonCode, string message)
        => new(SoloTaskResultKind.Partial, message, reasonCode);

    /// <summary>
    /// 业务失败。
    /// </summary>
    /// <param name="reasonCode">机器可读的失败原因码。</param>
    /// <param name="message">面向用户的失败原因。</param>
    public static SoloTaskResult Failed(string reasonCode, string message)
        => new(SoloTaskResultKind.Failed, message, reasonCode);

    /// <summary>
    /// 条件不满足，未执行。
    /// </summary>
    /// <param name="reasonCode">机器可读的跳过原因码。</param>
    /// <param name="message">面向用户的跳过原因。</param>
    public static SoloTaskResult Skipped(string reasonCode, string message)
        => new(SoloTaskResultKind.Skipped, message, reasonCode);
}
