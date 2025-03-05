using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 自动钓鱼
/// </summary>
public class FishingHandler : IActionHandler
{
    private AutoFishingTask _autoFishingTask = new(new AutoFishingTaskParam(300, 15, FishingTimePolicy.All, false));   // todo 做成可由脚本作者传入

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        // 钓鱼
        await _autoFishingTask.Start(ct);

        await Delay(1000, ct);
    }
}