using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Handler.Parameters;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Job;

using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 处理游戏时钟修改时间的逻辑 / Handles the execution logic for the in-game clock time modification action.
/// </summary>
public class SetTimeHandler : IActionHandler
{
    private readonly SetTimeTask _setTimeTask = new();

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("执行动作: 【修改时间】");

        if (!SetTimeOptions.TryParse(waypointForTrack?.ActionParams, out var options) || options == null)
        {
            return;
        }

        await _setTimeTask.DoOnce(options.Hour, options.Minute, ct, options.SkipAnimation);
    }
}
