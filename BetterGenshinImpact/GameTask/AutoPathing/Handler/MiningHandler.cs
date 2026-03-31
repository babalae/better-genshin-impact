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
/// 处理挖矿及伴随拾取动作的执行逻辑 / Handles the execution logic for mining and subsequent pick-up.
/// 包含多种角色的特有破矿连招宏 / Contains dynamic macro arrays for character-specific mining movesets.
/// </summary>
public class MiningHandler : IActionHandler
{
    private readonly string[] _miningActions =
    [
        "爱诺 attack(0.8)",
        "诺艾尔 attack(1.25)",
        "玛薇卡 attack(0.20),j,wait(0.5),attack(0.6)",
        "迪希雅 attack(0.6),mousedown,wait(2.1),mouseup,j",
        "娜维娅 attack(1.25)",
        "辛焱 attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8)",
        "重云 attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8)",
        "荒泷一斗 attack(0.1),charge(1.9),j,wait(0.5),attack(0.2)",
        "基尼奇 attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8)",
        "菲米尼 attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8)",
        "卡维 attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8)",
        "优菈 attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8)",
        "嘉明 attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8)",
        "多莉 attack(2.0)",
        "北斗 attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8),attack(0.28),jump,wait(0.8)",
        "早柚 attack(0.23),j,wait(0.6),attack(0.23),j,wait(0.6),attack(0.23),j,wait(0.6),attack(0.23),j,wait(0.6)",
        "迪卢克 charge(3.15),j",
        "坎蒂丝 e(hold,wait)",
        "雷泽 e(hold,wait)",
        "凝光 attack(4.0)",
        "钟离 e(hold,wait)"
    ];

    private readonly ScanPickTask _scanPickTask = new();

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("执行动作: 【执行挖矿】 / Executing action: [Mining]");
        var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
        if (combatScenes == null)
        {
            Logger.LogError("队伍识别未初始化成功，无法进行队伍挖矿调度！/ Party recognition uninitialized.");
            return;
        }

        ExecuteMiningMoveset(combatScenes);

        bool pickupDisabled = waypointForTrack?.ActionParams?.Contains("disablePickupAround", StringComparison.OrdinalIgnoreCase) == true;
        if (!pickupDisabled)
        {
            await Delay(1000, ct);
            await _scanPickTask.Start(ct);
        }
    }

    private void ExecuteMiningMoveset(CombatScenes combatScenes)
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
                        Logger.LogDebug("选用 {AvatarName} 进行挖矿作业", avatar.Name);
                        command.Execute(combatScenes);
                        foundAvatar = true;
                    }
                }
                
                if (foundAvatar)
                {
                    break;
                }
            }
            
            if (!foundAvatar)
            {
                Logger.LogWarning("当前队伍中没有包含内置的挖矿角色阵列，将跳过破矿连招！ / No supported mining avatars found in party.");
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "挖矿宏指令执行期间发生非预期异常 / Exception occurred during mining execution.");
        }
    }
}

