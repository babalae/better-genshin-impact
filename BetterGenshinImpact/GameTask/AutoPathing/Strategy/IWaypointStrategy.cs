using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy;

public interface IWaypointStrategy
{
    Task<bool> ExecuteAsync(PathExecutor executor, WaypointForTrack waypoint, List<List<WaypointForTrack>> waypointsList);
}
