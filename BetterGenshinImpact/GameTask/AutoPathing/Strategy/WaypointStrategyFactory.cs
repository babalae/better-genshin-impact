using System;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy;

public static class WaypointStrategyFactory
{
    private static readonly TeleportWaypointStrategy _teleportStrategy = new();
    private static readonly MovementWaypointStrategy _movementStrategy = new();

    public static IWaypointStrategy GetStrategy(string waypointTypeCode)
    {
        if (waypointTypeCode == WaypointType.Teleport.Code)
        {
            return _teleportStrategy;
        }

        return _movementStrategy;
    }
}
