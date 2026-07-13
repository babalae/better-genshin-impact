using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public static class CombatScriptExecutor
{
    /// <summary>
    /// 执行简易战斗策略脚本。
    /// </summary>
    /// <param name="combatScript">已解析的策略</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="logger">日志</param>
    /// <param name="combatScenes">
    /// 可选。传了则使用现成的 CombatScenes（由调用方管理生命周期）；
    /// 不传则在内部通过截图自动创建并管理生命周期。
    /// </param>
    public static async Task ExecuteAsync(
        CombatScript combatScript,
        CancellationToken ct,
        ILogger logger,
        CombatScenes? combatScenes = null)
    {
        var ownsScenes = false;
        if (combatScenes == null)
        {
            using var capture = CaptureToRectArea();
            combatScenes = new CombatScenes();
            combatScenes.InitializeTeam(capture);
            if (!combatScenes.CheckTeamInitialized())
            {
                logger.LogError("队伍识别未初始化成功！");
                combatScenes.Dispose();
                return;
            }
            ownsScenes = true;
        }

        try
        {
            combatScenes.BeforeTask(ct);

            // 提前校验是否存在策略要求的角色
            if (!combatScript.AvatarNames.Contains(CombatScriptParser.CurrentAvatarName))
            {
                bool hasAvatar = combatScenes.GetAvatars().Any(avatar => combatScript.AvatarNames.Contains(avatar.Name));
                if (!hasAvatar)
                {
                    logger.LogError("简易策略脚本要求的角色不存在！队伍中需要存在下面角色中的一个或多个：{AvatarNames}", string.Join(", ", combatScript.AvatarNames));
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
                logger.LogWarning("简易策略脚本执行时出现重试异常，原因：{Msg}，重试中...", e.Message);
                throw;
            }
            catch (Exception e)
            {
                logger.LogError(e, "执行简易策略脚本时发生错误！");
            }
        }
        finally
        {
            if (ownsScenes)
            {
                combatScenes.Dispose();
            }
        }
    }
}
