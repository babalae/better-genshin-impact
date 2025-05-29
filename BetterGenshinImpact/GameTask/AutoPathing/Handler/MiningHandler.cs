using System;
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
        荒泷一斗 attack(2.0)
        迪希雅 attack(2.0)
        玛薇卡 attack(2.0)
        基尼奇 attack(2.0)
        娜维娅 attack(2.0)
        菲米尼 attack(2.0)
        迪卢克 attack(2.0)
        诺艾尔 attack(2.0)
        卡维 attack(2.0)
        雷泽 attack(2.0)
        优菈 attack(2.0)
        嘉明 attack(2.0)
        辛焱 attack(2.0)
        重云 attack(2.0)
        多莉 attack(2.0)
        北斗 attack(2.5)
        早柚 attack(2.5)
        坎蒂丝 e(hold,wait)
        雷泽 e(hold,wait)
        钟离 e(hold,wait)
        凝光 attack(4.0)
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