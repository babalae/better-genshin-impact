using BetterGenshinImpact.Core.Input;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 停止飞行（下落攻击）
/// </summary>
/// <param name="runtime">本次运行使用的运行环境</param>
public class StopFlyingHandler(GameRuntime runtime) : IActionHandler
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime = runtime;

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        // 如果有参数，先自由落体，然后恢复飞行
        if (waypointForTrack != null
            && !string.IsNullOrEmpty(waypointForTrack.ActionParams)
            && int.TryParse(waypointForTrack.ActionParams, out var stopFlyingWaitTime))
        {
            InputHub.Foreground.SimulateAction(GIActions.Jump);
            await _runtime.Delay(stopFlyingWaitTime, ct);
            InputHub.Foreground.SimulateAction(GIActions.Jump);
            await _runtime.Delay(300, ct);
        }

        // 下落攻击接近目的地
        Logger.LogInformation("动作：下落攻击");
        InputHub.Foreground.SimulateAction(GIActions.NormalAttack);
        int i;
        for (i = 0; i < 50; i++)
        {
            using var screen = _runtime.CaptureToRectArea();
            var isFlying = Bv.GetMotionStatus(screen) == MotionStatus.Fly;
            if (isFlying)
            {
                await _runtime.Delay(300, ct);
            }
            else
            {
                break;
            }
        }

        if (i == 50)
        {
            Logger.LogWarning("动作：下落攻击 超时结束");
        }
        else
        {
            Logger.LogInformation("动作：下落攻击 结束");
        }
    }
}
