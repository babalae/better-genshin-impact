using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 独立任务合同。一个实例绑定一个运行环境，只运行一次。
/// 构造函数只接收 GameRuntime 和可从 DI 解析的服务，业务参数全部通过 <typeparamref name="TParam"/> 传入。
/// </summary>
/// <typeparam name="TParam">本次运行的全部业务参数，必须能 JSON 往返。</typeparam>
/// <typeparam name="TData">附加结果数据；没有时使用 <see cref="NoData"/>。</typeparam>
public interface ISoloTask<TParam, TData>
    where TParam : class, new()
    where TData : class
{
    /// <summary>
    /// 运行任务。业务失败返回 Failed 结果；取消时抛出 OperationCanceledException。
    /// </summary>
    /// <param name="param">本次运行的业务参数。</param>
    /// <param name="ct">本次运行的取消令牌。</param>
    /// <returns>任务结果。</returns>
    Task<SoloTaskResult<TData>> RunAsync(TParam param, CancellationToken ct);
}
