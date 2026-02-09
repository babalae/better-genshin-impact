using BetterGenshinImpact.Helpers;
﻿using System;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 触发普通攻击
/// </summary>
[Obsolete]
public class NormalAttackHandler : IActionHandler
{
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation(Lang.S["GameTask_11069_7cad94"], "普通攻击");
        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
        await Delay(1000, ct);
    }
}
