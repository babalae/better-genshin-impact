using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Hosting;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

/// <summary>
/// 在任务运行期间驱动网络探测，避免依赖任务自身的检查点。
/// </summary>
public sealed class NetworkHealthMonitorHostedService(
    INetworkHealthMonitor networkHealthMonitor,
    IRecoverySession recoverySession) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (TaskControl.TaskSemaphore.CurrentCount == 0 || recoverySession.CurrentTask is not null)
            {
                // 用任务取消令牌而非应用生命周期令牌：任务被取消时恢复流程必须一起停，
                // 否则它会继续截图与发送键鼠输入，和随后启动的任务抢操作权。
                networkHealthMonitor.RequestCheck(GetTaskCancellationToken(stoppingToken));
            }
        }
    }

    /// <summary>任务取消令牌。Clear() 可能并发释放 CTS，此时退回应用令牌。</summary>
    private static CancellationToken GetTaskCancellationToken(CancellationToken fallback)
    {
        try
        {
            return CancellationContext.Instance.Cts.Token;
        }
        catch (ObjectDisposedException)
        {
            return fallback;
        }
    }
}
