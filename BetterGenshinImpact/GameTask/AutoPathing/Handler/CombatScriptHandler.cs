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
/// 处理简易战斗策略脚本（CombatScript）动作的执行逻辑。
/// </summary>
/// <remarks>
/// 主要根据航点（Waypoint）参数中携带的指令集，动态解析并同步到游戏内执行相关战斗操作。
/// 适用于轻量级战斗、解谜触发或是无需完整 AutoFight 逻辑支持的快速技能交互场景。
/// </remarks>
public class CombatScriptHandler : IActionHandler
{
    /// <summary>
    /// 异步执行简易策略脚本动作。
    /// </summary>
    /// <param name="ct">用于取消异步操作的取消令牌（CancellationToken）。</param>
    /// <param name="waypointForTrack">触发脚本执行的当前航点配置，必须包含有效的 <see cref="WaypointForTrack.CombatScript"/> 属性。</param>
    /// <param name="config">透传的额外配置项参数（在该处理器中当前未使用）。</param>
    /// <returns>代表异步执行流程的任务实例。若脚本不符合条件或执行失败会提前记录错误日志并中断。</returns>
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

    /// <summary>
    /// 验证当前队伍配置中是否存在执行该脚本所要求的必需上阵角色。
    /// </summary>
    /// <param name="combatScript">包含目标角色名称列表的战斗脚本业务模型。</param>
    /// <param name="combatScenes">当前正在获取与执行的战斗场景上下文信息，用于识别在场及后台队伍信息。</param>
    /// <returns>若当前上阵角色或处于队伍后台的角色中包含所需执行角色，则返回 <c>true</c>；否则输出警告记录并返回 <c>false</c> 以直接中断脚本流转。</returns>
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

    /// <summary>
    /// 遍历、校验下发脚本内部的所有动作指令并于场景上下文（Context）内依序同步触发。
    /// </summary>
    /// <param name="ct">异步操作取消令牌（CancellationToken），将在每条指令同步开始前执行断言（ThrowIfCancellationRequested）以在需要时安全中断与隐式退出。</param>
    /// <param name="combatScript">已封装多条业务指令 <see cref="ICombatCommand"/> 的有序指令合集对象。</param>
    /// <param name="combatScenes">提供角色按键响应、冷却识别、状态监控支持的核心场景交互依赖池。</param>
    /// <exception cref="RetryException">当在业务逻辑进行期间触发不可靠或意外状态（如识别失败）所需进入安全状态回滚与重新发起时抛出。</exception>
    /// <exception cref="OperationCanceledException">收到外部强制取消请求并跳过所有剩余执行块直接外抛退出逻辑节点时产生。</exception>
    /// <remarks>
    /// 作为基础底层封装隔离操作异常：仅重抛出任务层需要做调度的重试及中断类异常信息，针对普通代码层未捕获级致命故障打印后吞没防污染线程。
    /// </remarks>
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
            Logger.LogWarning("简易策略脚本执行时触发重试：{Msg}", e.Message);
            throw; // 向上传递以触发外层的重试机制
        }
        catch (OperationCanceledException)
        {
            throw; // 任务被取消，正常向上传递
        }
        catch (Exception e)
        {
            Logger.LogError(e, "执行简易策略脚本期间发生严重错误");
            throw; // 捕获基类异常并记录日志后重新抛出，避免静默失败
        }
    }
}

