using BetterGenshinImpact.Helpers;
﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
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
            Logger.LogInformation(Lang.S["GameTask_11069_7cad94"], "简易策略脚本");
            var combatScript = waypointForTrack.CombatScript;
            var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
            if (combatScenes == null)
            {
                Logger.LogError(Lang.S["GameTask_11074_f6bb4a"]);
                return;
            }

            // 设置取消令牌到 CombatScenes 和 Avatar 对象
            combatScenes.BeforeTask(ct);


            // 提前校验是否存在策略要求的角色
            if (!combatScript.AvatarNames.Contains(CombatScriptParser.CurrentAvatarName))
            {
                bool hasAvatar = combatScenes.GetAvatars().Any(avatar => combatScript.AvatarNames.Contains(avatar.Name));
                if (!hasAvatar)
                {
                    Logger.LogError(Lang.S["GameTask_11073_28b514"], string.Join(", ", combatScript.AvatarNames));
                    return;
                }
            }

            try
            {
                // 通用化战斗策略
                for (var i = 0; i < combatScript.CombatCommands.Count; i++)
                {
                    var command = combatScript.CombatCommands[i];
                    var lastCommand = i == 0 ? command : combatScript.CombatCommands[i - 1];
                    ct.ThrowIfCancellationRequested();
                    command.Execute(combatScenes, lastCommand);
                }
            }
            catch (RetryException e)
            {
                Logger.LogWarning(Lang.S["GameTask_11072_5b2892"], e.Message);
                throw;
            }
            catch (Exception e)
            {
                Logger.LogError(e, Lang.S["GameTask_11071_767737"]);
            }
        }
        else
        {
            Logger.LogError(Lang.S["GameTask_11070_302f28"]);
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