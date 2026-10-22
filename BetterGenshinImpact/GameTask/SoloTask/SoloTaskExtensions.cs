using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 独立任务的调用辅助方法。
/// </summary>
public static class SoloTaskExtensions
{
    /// <summary>
    /// 运行任务，并把任务内部抛出的 <see cref="SoloTaskAbortException"/> 转换为结果。
    /// 需要拿到明确结果的入口（TaskRunner、任务计划、任务内部组合）使用它；需要保留旧“正常结束”异常语义的入口直接调用 RunAsync。
    /// </summary>
    /// <param name="task">要运行的任务。</param>
    /// <param name="param">本次运行的业务参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>任务结果。</returns>
    public static async Task<SoloTaskResult<TData>> ExecuteAsync<TParam, TData>(
        this ISoloTask<TParam, TData> task, TParam param, CancellationToken ct)
        where TParam : class, new()
        where TData : class
    {
        try
        {
            return await task.RunAsync(param, ct);
        }
        catch (SoloTaskAbortException e)
        {
            return e.Result;
        }
    }
}
