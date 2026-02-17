using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Job;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

public class EnterAndExitWonderlandHandler : IActionHandler
{
    private readonly EnterAndExitWonderlandJob _enterAndExitWonderlandJob = new();

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        await _enterAndExitWonderlandJob.Start(ct);
    }
}