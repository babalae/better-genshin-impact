using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Job;

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
        if (string.IsNullOrWhiteSpace(waypointForTrack?.ActionParams))
        {
            return;
        }

        string[] timeParts = waypointForTrack.ActionParams.Split(':');
        if (timeParts.Length < 2)
        {
            return;
        }

        if (!int.TryParse(timeParts[0], out int hour) || !int.TryParse(timeParts[1], out int minute))
        {
            return;
        }

        bool skipAnimation = timeParts.Length < 3 || (bool.TryParse(timeParts[2], out var skip) && skip);
        await _setTimeTask.DoOnce(hour, minute, ct, skipAnimation);
    }
}
