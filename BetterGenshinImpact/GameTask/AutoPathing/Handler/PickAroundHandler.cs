using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 采集任务到达点位后执行拾取操作
/// 暂时不需要
/// </summary>
public class PickAroundHandler : IActionHandler
{
    public async Task RunAsync(CancellationTokenSource cts)
    {
        await Task.Delay(1000, cts.Token);
        throw new NotImplementedException();
    }
}
