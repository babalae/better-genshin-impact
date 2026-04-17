using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy.Movement;

/// <summary>
/// 奔跑（提速）模式处理器 / Elevated running logic
/// </summary>
public class RunMoveModeHandler : IMoveModeHandler
{
    public bool CanHandle(string moveModeCode) => moveModeCode == MoveModeEnum.Run.Code;

    public Task<MoveModeResult> ExecuteAsync(WaypointForTrack waypoint, PathingMovementContext context)
    {
        var targetFastMode = context.Distance > 20;
        if (targetFastMode != context.FastMode)
        {
            Simulation.SendInput.SimulateAction(GIActions.SprintMouse, targetFastMode ? KeyType.KeyDown : KeyType.KeyUp);
            context.FastMode = targetFastMode;
        }

        return Task.FromResult(MoveModeResult.Pass);
    }
}
