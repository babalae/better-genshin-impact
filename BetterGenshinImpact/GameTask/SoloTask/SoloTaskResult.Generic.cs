namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 任务返回的强类型结果。
/// </summary>
/// <typeparam name="TData">附加数据类型。</typeparam>
/// <param name="Kind">结果种类。</param>
/// <param name="Message">面向用户的原因或摘要。</param>
/// <param name="ReasonCode">机器可读的原因码。</param>
/// <param name="Data">附加数据。</param>
public sealed record SoloTaskResult<TData>(SoloTaskResultKind Kind, string Message, string? ReasonCode, TData? Data)
    : ISoloTaskResult where TData : class
{
    /// <inheritdoc />
    object? ISoloTaskResult.Data => Data;

    /// <summary>
    /// 不带数据的结果可以直接作为任意 TData 的结果返回。
    /// </summary>
    /// <param name="result">不带数据的结果。</param>
    public static implicit operator SoloTaskResult<TData>(SoloTaskResult result)
        => new(result.Kind, result.Message, result.ReasonCode, null);
}
