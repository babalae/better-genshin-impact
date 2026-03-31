using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 触发普通攻击的动作处理器 (已废弃) / Handler for triggering normal attack action (Obsolete).
/// 此硬编码操作已不推荐使用，将被配置化战斗指令集替代。
/// </summary>
[Obsolete("该动作类型已被移除或降级，使用配置化战斗替代 / Obsolete: Use configured combat scripts instead")]
public class NormalAttackHandler : IActionHandler
{
    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("执行动作: 【普通攻击】 / Executing action: [Normal Attack]");
        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
        await Delay(1000, ct);
    }
}
