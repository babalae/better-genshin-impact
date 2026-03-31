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
/// 触发元素战技的动作处理器 (已废弃) / Handler for triggering elemental skill action (Obsolete).
/// 此硬编码操作已不推荐使用，将被配置化战技逻辑替代。
/// </summary>
[Obsolete("该类动作已被更智能的战技配置取代 / Replaced by more intelligent skill configuration")]
public class ElementalSkillHandler : IActionHandler
{
    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("执行动作: 【释放元素战技】 / Executing action: [Elemental Skill]");
        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
        await Delay(1000, ct);
    }
}
