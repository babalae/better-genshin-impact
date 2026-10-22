using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 在任务内部较深的位置提前结束任务，并携带明确结果。
/// 继承 <see cref="NormalEndException"/>：直接调用 RunAsync 的旧入口（JS、旧一条龙、调度器）保持原来的“正常结束”语义；
/// 通过 <see cref="SoloTaskExtensions.ExecuteAsync{TParam,TData}"/>、TaskRunner 或任务计划运行时，会被转换成任务结果。
/// </summary>
/// <param name="result">任务的最终结果。</param>
public sealed class SoloTaskAbortException(SoloTaskResult result) : NormalEndException(result.Message)
{
    /// <summary>
    /// 任务的最终结果。
    /// </summary>
    public SoloTaskResult Result { get; } = result;
}
