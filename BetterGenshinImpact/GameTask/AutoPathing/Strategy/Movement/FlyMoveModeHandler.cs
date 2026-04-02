using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using System.Diagnostics;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy.Movement;

/// <summary>
/// 飞行移动的动作处理器 / Airborne gliding navigation logic
/// </summary>
public class FlyMoveModeHandler : IMoveModeHandler
{
    public bool CanHandle(string moveModeCode) => moveModeCode == MoveModeEnum.Fly.Code;

    public async Task<MoveModeResult> ExecuteAsync(WaypointForTrack waypoint, PathingMovementContext context)
    {
        var isFlying = Bv.GetMotionStatus(context.Screen) == MotionStatus.Fly;
        if (!isFlying)
        {
            Debug.WriteLine("未进入飞行状态，按下空格");
            Simulation.SendInput.SimulateAction(GIActions.Jump);
            await Delay(200, context.CancellationToken);
        }

        await Delay(100, context.CancellationToken);
        return MoveModeResult.Continue;
    }
}
