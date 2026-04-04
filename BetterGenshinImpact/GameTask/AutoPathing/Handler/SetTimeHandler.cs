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

        string actionParams = waypointForTrack.ActionParams;
        int firstColon = actionParams.IndexOf(':');
        if (firstColon < 0) return;

        int secondColon = actionParams.IndexOf(':', firstColon + 1);
        
        string hourStr = actionParams.Substring(0, firstColon);
        string minuteStr = secondColon < 0 
            ? actionParams.Substring(firstColon + 1) 
            : actionParams.Substring(firstColon + 1, secondColon - firstColon - 1);

        if (!int.TryParse(hourStr, out int hour) || !int.TryParse(minuteStr, out int minute))
        {
            return;
        }

        bool skipAnimation = true;
        if (secondColon >= 0)
        {
            string skipStr = actionParams.Substring(secondColon + 1);
            skipAnimation = bool.TryParse(skipStr, out bool skip) && skip;
        }

        await _setTimeTask.DoOnce(hour, minute, ct, skipAnimation);
    }
}
