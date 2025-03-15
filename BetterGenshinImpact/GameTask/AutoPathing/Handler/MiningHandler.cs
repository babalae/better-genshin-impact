using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
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
                                                                                        钟离 e(hold)
                                                                                        雷泽 e(hold)
                                                                                        卡齐娜 e(hold),keydown(s),wait(0.4),keyup(s),attack(0.2),attack(0.2),attack(0.2),attack(0.2),attack(0.2),attack(0.2)
                                                                                        坎蒂丝 e(hold)
                                                                                        凝光 a(0.2),a(0.2),a(0.2),a(0.2),a(0.2),a(0.2),a(0.2),a(0.2)
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

        await Delay(1000, ct);

        // 拾取
        await _scanPickTask.Start(ct);
    }

    private void Mining(CombatScenes combatScenes)
    {
        try
        {
            // 通用化战斗策略
            foreach (var command in _miningCombatScript.CombatCommands)
            {
                command.Execute(combatScenes);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
            Debug.WriteLine(e.StackTrace);
        }
    }
}