using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Remote;

/// <summary>
/// Pulonia 远程节点的实例通信通道，由多实例服务实现。主实例用前三个方法派发，子实例用后两个方法回报
/// </summary>
public interface IPuloniaRemoteTransport
{
    /// <summary>
    /// 主实例：某个子实例断开连接，参数为子实例进程 ID。可能在管道后台线程触发
    /// </summary>
    event Action<int>? RemoteInstanceDisconnected;

    /// <summary>
    /// 子实例：与主实例的连接已断开。可能在管道后台线程触发
    /// </summary>
    event Action? RootConnectionLost;

    /// <summary>
    /// 主实例：把执行目标解析为当前在线子实例的进程 ID
    /// </summary>
    /// <param name="target">执行目标</param>
    /// <returns>在线时返回进程 ID，否则为 null</returns>
    int? ResolveTargetProcessId(PuloniaTaskExecutionTarget target);

    /// <summary>
    /// 主实例：请求子实例开始执行节点，子实例确认接受后返回
    /// </summary>
    Task StartRemoteNodeAsync(int processId, PuloniaRemoteNodeStart start, CancellationToken ct);

    /// <summary>
    /// 主实例：请求子实例取消节点
    /// </summary>
    Task CancelRemoteNodeAsync(int processId, Guid executionId, CancellationToken ct);

    /// <summary>
    /// 子实例：转发完成事件，主实例持久化后返回；主实例拒绝时抛出异常
    /// </summary>
    Task ReportRemoteNodeAsync(PuloniaRemoteNodeReport report, CancellationToken ct);

    /// <summary>
    /// 子实例：回报节点结束
    /// </summary>
    Task FinishRemoteNodeAsync(PuloniaRemoteNodeFinished finished, CancellationToken ct);
}
