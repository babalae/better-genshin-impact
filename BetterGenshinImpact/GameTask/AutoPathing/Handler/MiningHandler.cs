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
    private readonly CombatScript _miningCombatScript = CombatScriptParser.ParseContext("""
        荒泷一斗 attack(1.5)
        迪希雅 attack(1.5)
        玛薇卡 attack(1.5)
        基尼奇 attack(1.5)
        娜维娅 attack(1.5)
        菲米尼 attack(1.5)
        迪卢克 attack(1.5)
        诺艾尔 attack(1.5)
        多莉 attack(1.5)
        卡维 attack(1.5)
        早柚 attack(1.5)
        雷泽 attack(1.5)
        优菈 attack(1.5)
        嘉明 attack(1.5)
        辛焱 attack(1.5)
        重云 attack(1.5)
        北斗 attack(1.5)
        卡齐娜 e(hold,wait),keydown(s),wait(0.4),keyup(s),attack(1.5)
        坎蒂丝 e(hold,wait)
        雷泽 e(hold,wait)
        钟离 e(hold,wait)
        凝光 attack(2.0)
        """);

    private readonly ScanPickTask _scanPickTask = new();

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
        if (combatScenes == null)
        {
            Logger.LogError("队伍识别未初始化成功！");
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
            // 通用化战斗策略
            foreach (var command in _miningCombatScript.CombatCommands)
            {
                var avatar = combatScenes.SelectAvatar(command.Name);
                if (avatar != null)
                {
                    command.Execute(combatScenes);
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