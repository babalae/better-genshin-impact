using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

public class CombatScriptHandler : IActionHandler
{
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        if (waypointForTrack is { CombatScript: not null })
        {
            Logger.LogInformation("执行 {Text}", "战斗策略脚本");
            var combatScript = waypointForTrack.CombatScript;
            var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
            if (combatScenes == null)
            {
                Logger.LogError("队伍识别未初始化成功！");
                return;
            }


            try
            {
                // 通用化战斗策略
                foreach (var command in combatScript.CombatCommands)
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
        else
        {
            Logger.LogError("战斗脚本action_params内容为空");
        }
    }

    // private static bool IsOnlyCurrentAvatar(CombatScript combatScript)
    // {
    //     var isCurrentAvatar = false;
    //     if (combatScript.AvatarNames.Count == 1)
    //     {
    //         foreach (var avatarName in combatScript.AvatarNames)
    //         {
    //             if (avatarName == CombatScriptParser.CurrentAvatarName)
    //             {
    //                 isCurrentAvatar = true;
    //                 break;
    //             }
    //         }
    //     }
    //
    //     return isCurrentAvatar;
    // }
}