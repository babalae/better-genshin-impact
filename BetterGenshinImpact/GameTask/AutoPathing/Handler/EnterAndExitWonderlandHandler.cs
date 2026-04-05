using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Job;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 处理进出秘境)动作的执行逻辑 / Handles the execution logic for entering and exiting wonderland (domain) action.
/// </summary>
public class EnterAndExitWonderlandHandler : IActionHandler
{
    private readonly EnterAndExitWonderlandJob _enterAndExitWonderlandJob = new();

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("执行动作: 【进出幻秘境】");
        await _enterAndExitWonderlandJob.Start(ct);
    }
}
