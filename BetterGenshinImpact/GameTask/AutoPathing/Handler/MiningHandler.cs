using BetterGenshinImpact.Helpers;
﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Job;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 挖矿并拾取
/// </summary>
public class MiningHandler : IActionHandler
{
    private readonly string[] _miningActions =
    [
        Lang.S["GameTask_11131_5759b3"],
        Lang.S["GameTask_11130_5dd485"],
        Lang.S["GameTask_11129_42f87a"],
        Lang.S["GameTask_11128_12572e"],
        Lang.S["GameTask_11127_d4635d"],
        Lang.S["GameTask_11126_3ded08"],
        Lang.S["GameTask_11125_9e6c85"],
        Lang.S["GameTask_11124_01ebc6"],
        Lang.S["GameTask_11123_93161b"],
        Lang.S["GameTask_11122_21f83e"],
        Lang.S["GameTask_11121_cf929b"],
        Lang.S["GameTask_11120_1fa42f"],
        Lang.S["GameTask_11119_316eea"],
        Lang.S["GameTask_11118_a5680c"],
        Lang.S["GameTask_11117_96c2d8"],
        Lang.S["GameTask_11116_08a1f1"],
        Lang.S["GameTask_11115_8a2791"],
        Lang.S["GameTask_11114_d74d37"],
        Lang.S["GameTask_11113_79f01a"],
        Lang.S["GameTask_11112_827e70"],
        Lang.S["GameTask_11111_0cfe4a"]
    ];
    

    private readonly ScanPickTask _scanPickTask = new();

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
        if (combatScenes == null)
        {
            Logger.LogError(Lang.S["GameTask_11074_f6bb4a"]);
            return;
        }

        // 挖矿
        Mining(combatScenes);


        if (waypointForTrack is { ActionParams: not null }
            && waypointForTrack.ActionParams.Contains("disablePickupAround",
                StringComparison.InvariantCultureIgnoreCase))
        {
            await Delay(1000, ct);

            // 拾取
            await _scanPickTask.Start(ct);
        }
    }

    private void Mining(CombatScenes combatScenes)
    {
        try
        {
            bool foundAvatar = false;
            foreach (var miningActionStr in _miningActions)
            {
                var miningAction = CombatScriptParser.ParseContext(miningActionStr);
                foreach (var command in miningAction.CombatCommands)
                {
                    var avatar = combatScenes.SelectAvatar(command.Name);
                    if (avatar != null)
                    {
                        command.Execute(combatScenes);
                        foundAvatar = true;
                    }
                }
                if (foundAvatar)
                {
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
            Debug.WriteLine(e.StackTrace);
        }
    }
}
