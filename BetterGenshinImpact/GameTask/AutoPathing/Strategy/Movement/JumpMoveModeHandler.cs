using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy.Movement;

/// <summary>
/// 跳跃移动模式处理器 / Parabolic jumping locomotion
/// </summary>
public class JumpMoveModeHandler : IMoveModeHandler
{
    public bool CanHandle(string moveModeCode) => moveModeCode == MoveModeEnum.Jump.Code;

    public async Task<MoveModeResult> ExecuteAsync(WaypointForTrack waypoint, PathingMovementContext context)
    {
        Simulation.SendInput.SimulateAction(GIActions.Jump);
        await Delay(200, context.CancellationToken);
        
        return MoveModeResult.Continue;
    }
}
