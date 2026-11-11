using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Instance;

namespace BetterGenshinImpact.Pulonia.Remote;

/// <summary>
/// 一步下发（<c>pulonia.run.*</c>）的实例通信通道，由多实例服务实现，Pulonia 层只依赖这个接口。
/// 主实例用 <see cref="SubmitRunAsync"/>、<see cref="CancelRunAsync"/> 与 <see cref="ChildInstanceDisconnected"/>；
/// 子实例用 <see cref="ReportRunChangedAsync"/> 与 <see cref="RootConnected"/>。事件可能在管道后台线程触发，订阅方不能阻塞。
/// </summary>
public interface IPuloniaRunTransport
{
    /// <summary>
    /// 主实例：某个子实例断开连接，参数为它登记时的端点。
    /// </summary>
    event Action<InstanceEndpoint>? ChildInstanceDisconnected;

    /// <summary>
    /// 子实例：与主实例的连接登记成功（首次连接和每次重新连接后都触发）。
    /// </summary>
    event Action? RootConnected;

    /// <summary>
    /// 主实例：请求子实例接受一次下发；子实例校验修订号并入队后返回运行 ID。
    /// </summary>
    /// <param name="target">在线子实例</param>
    /// <param name="request">下发内容</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>子实例的响应</returns>
    /// <exception cref="InvalidOperationException">当前不是主实例、子实例不在线或子实例拒绝（原因在消息中）</exception>
    Task<PuloniaRunSubmitResponse> SubmitRunAsync(InstanceEndpoint target, PuloniaRunSubmitRequest request, CancellationToken ct);

    /// <summary>
    /// 主实例：请求子实例停止一个由主实例下发的运行；子实例发出取消后立即响应。
    /// </summary>
    /// <param name="target">在线子实例</param>
    /// <param name="request">要停止的运行</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="InvalidOperationException">当前不是主实例、子实例不在线或子实例拒绝</exception>
    Task CancelRunAsync(InstanceEndpoint target, PuloniaRunCancelRequest request, CancellationToken ct);

    /// <summary>
    /// 子实例：向主实例上报一个主实例下发的运行的最新状态。
    /// </summary>
    /// <param name="report">运行状态</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="InvalidOperationException">尚未连接主实例或主实例拒绝</exception>
    Task ReportRunChangedAsync(PuloniaRunChangedReport report, CancellationToken ct);
}
