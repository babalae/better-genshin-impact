using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Remote;

/// <summary>
/// 主实例协调器把节点交给子实例执行的入口
/// </summary>
public interface IPuloniaRemoteNodeDispatcher
{
    /// <summary>
    /// 执行目标当前是否在线
    /// </summary>
    /// <param name="target">执行目标</param>
    /// <returns>在线时返回 true</returns>
    bool IsAvailable(PuloniaTaskExecutionTarget target);

    /// <summary>
    /// 在执行实例上执行一个节点并等待结束回报。取消时请求子实例取消，并等待它确认执行器退出
    /// </summary>
    /// <param name="target">执行目标</param>
    /// <param name="start">节点启动信息</param>
    /// <param name="reporter">执行器完成事件的落盘回调；账号准备时为 null</param>
    /// <param name="ct">节点取消令牌</param>
    /// <returns>子实例的结束回报</returns>
    /// <exception cref="OperationCanceledException">已取消，且子实例确认退出</exception>
    /// <exception cref="InvalidOperationException">执行实例不在线或拒绝执行</exception>
    /// <exception cref="System.IO.IOException">执行期间执行实例断开</exception>
    /// <exception cref="TimeoutException">请求未确认，或取消后执行实例没有在限定时间内确认退出</exception>
    Task<PuloniaRemoteNodeFinished> ExecuteAsync(PuloniaTaskExecutionTarget target, PuloniaRemoteNodeStart start,
        Func<PuloniaTaskCompletionEvent, CancellationToken, Task>? reporter, CancellationToken ct);
}
