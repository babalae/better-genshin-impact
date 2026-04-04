using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 处理强行停止飞行状态并施加下落攻击的动作逻辑 / Handles the execution logic for terminating flight via plunging attack action.
/// </summary>
public class StopFlyingHandler : IActionHandler
{
    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        if (waypointForTrack != null && !string.IsNullOrWhiteSpace(waypointForTrack.ActionParams) && 
            int.TryParse(waypointForTrack.ActionParams, out var stopFlyingWaitTime))
        {
            Simulation.SendInput.SimulateAction(GIActions.Jump);
            await Delay(stopFlyingWaitTime, ct);
            Simulation.SendInput.SimulateAction(GIActions.Jump);
            await Delay(300, ct);
        }

        Logger.LogInformation("动作: 【下落攻击以停止飞行】 / Action: [Plunge Attack to stop flying]");
        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);

        const int maxAttempts = 50;
        int i = 0;

        for (; i < maxAttempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            bool isFlying;
            using (var screen = CaptureToRectArea())
            {
                isFlying = Bv.GetMotionStatus(screen) == MotionStatus.Fly;
            }
            
            if (!isFlying)
            {
                break;
            }
            await Delay(300, ct);
        }

        if (i == maxAttempts)
        {
            Logger.LogWarning("动作：下落攻击未能在预计时间内完全落地(超时)");
        }
        else
        {
            Logger.LogInformation("动作：下落攻击正常结束，已落地");
        }
    }
}
