using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

public interface IActionHandler
{
    Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack=null, object? config = null);
}
