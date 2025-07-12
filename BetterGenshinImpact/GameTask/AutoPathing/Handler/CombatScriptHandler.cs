using System;
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
            Logger.LogInformation("执行 {Text}", "简易策略脚本");
            var combatScript = waypointForTrack.CombatScript;
            var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
            if (combatScenes == null)
            {
                Logger.LogError("队伍识别未初始化成功！");
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
                    Logger.LogError("简易策略脚本要求的角色不存在！队伍中需要存在下面角色中的一个或多个：{AvatarNames}", string.Join(", ", combatScript.AvatarNames));
                    return;
                }
            }

            try
            {
                // 通用化战斗策略
                foreach (var command in combatScript.CombatCommands)
                {
                    ct.ThrowIfCancellationRequested();
                    command.Execute(combatScenes);
                }
            }
            catch (RetryException e)
            {
                Logger.LogWarning("简易策略脚本执行时出现重试异常，原因：{Msg}，重试中...", e.Message);
                throw;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "执行简易策略脚本时发生错误！");
            }
        }
        else
        {
            Logger.LogError("策略脚本action_params内容为空");
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