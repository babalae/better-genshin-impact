using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 处理简易战斗策略脚本动作的执行逻辑 / Handles the execution logic for the simple combat script action.
/// 根据点位参数动态解析并执行指令集 / Dynamically parses and executes command sets based on waypoint parameters.
/// </summary>
public class CombatScriptHandler : IActionHandler
{
    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        if (waypointForTrack?.CombatScript == null)
        {
            Logger.LogError("简易策略脚本缺失：action_params 内容为空");
            return;
        }

        Logger.LogInformation("执行动作: 【简易策略脚本】");
        var combatScript = waypointForTrack.CombatScript;
        
        var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
        if (combatScenes == null)
        {
            Logger.LogError("队伍识别未初始化成功！");
            return;
        }

        combatScenes.BeforeTask(ct);

        if (!ValidateRequiredAvatars(combatScript, combatScenes))
        {
            return;
        }

        ExecuteCombatCommands(ct, combatScript, combatScenes);
    }

    private bool ValidateRequiredAvatars(CombatScript combatScript, CombatScenes combatScenes)
    {
        if (combatScript.AvatarNames.Contains(CombatScriptParser.CurrentAvatarName))
        {
            return true;
        }

        bool hasAvatar = combatScenes.GetAvatars().Any(avatar => combatScript.AvatarNames.Contains(avatar.Name));
        if (!hasAvatar)
        {
            Logger.LogError("简易策略脚本要求的角色不存在！队伍中需要存在以下角色中的至少一个: {AvatarNames} / Required avatar missing.", 
                            string.Join(", ", combatScript.AvatarNames));
            return false;
        }

        return true;
    }

    private void ExecuteCombatCommands(CancellationToken ct, CombatScript combatScript, CombatScenes combatScenes)
    {
        try
        {
            for (var i = 0; i < combatScript.CombatCommands.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var command = combatScript.CombatCommands[i];
                var lastCommand = i == 0 ? command : combatScript.CombatCommands[i - 1];
                
                command.Execute(combatScenes, lastCommand);
            }
        }
        catch (RetryException e)
        {
            Logger.LogWarning("简易策略脚本执行时触发重试：{Msg} / Retry triggered during combat script execution", e.Message);
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "执行简易策略脚本期间发生严重错误");
        }
    }
}

