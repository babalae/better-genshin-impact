using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Job;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

public class SetTimeHandler : IActionHandler
{
    private readonly SetTimeTask _setTimeTask = new();

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        if (waypointForTrack == null || string.IsNullOrEmpty(waypointForTrack.ActionParams)) return;
        
        string[] timeParts = waypointForTrack.ActionParams.Split(':');
        int hour = int.Parse(timeParts[0]);
        int minute = int.Parse(timeParts[1]);
        
        bool skipAnimation = timeParts.Length < 3 || bool.Parse(timeParts[2]);
        await _setTimeTask.DoOnce(hour, minute, ct, skipAnimation);
    }
}