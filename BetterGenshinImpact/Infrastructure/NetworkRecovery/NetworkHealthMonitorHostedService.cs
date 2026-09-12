using System;
using System.Threading;
using System.Threading.Tasks;
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
                networkHealthMonitor.RequestCheck(stoppingToken);
            }
        }
    }
}
