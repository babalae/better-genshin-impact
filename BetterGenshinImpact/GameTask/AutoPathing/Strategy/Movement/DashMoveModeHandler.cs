using System;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy.Movement;

/// <summary>
/// 冲刺移动模式处理器 / High-speed dash mechanics
/// </summary>
public class DashMoveModeHandler : IMoveModeHandler
{
    public bool CanHandle(string moveModeCode) => moveModeCode == MoveModeEnum.Dash.Code;

    public Task<MoveModeResult> ExecuteAsync(WaypointForTrack waypoint, PathingMovementContext context)
    {
        if (context.Distance > 20 && (DateTime.UtcNow - context.FastModeColdTime).TotalMilliseconds > 1000)
        {
            context.FastModeColdTime = DateTime.UtcNow;
            Simulation.SendInput.SimulateAction(GIActions.SprintMouse);
        }

        return Task.FromResult(MoveModeResult.Pass);
    }
}
