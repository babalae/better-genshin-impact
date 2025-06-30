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
        诺艾尔 attack,jump,wait(0.35),attack,jump,wait(0.35),attack,jump,wait(0.5)
        荒泷一斗 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        娜维娅 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        迪希雅 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        玛薇卡 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        基尼奇 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        菲米尼 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        迪卢克 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        卡维 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        优菈 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        嘉明 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        辛焱 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        重云 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        多莉 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        北斗 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        早柚 attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5),attack,jump,wait(0.5)
        坎蒂丝 e(hold,wait)
        雷泽 e(hold,wait)
        凝光 attack(4.0)
        钟离 e(hold,wait)
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
