using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

/// <summary>
/// 自动七圣召唤
/// </summary>
/// <param name="runtime">本次运行使用的运行环境</param>
/// <param name="config">配置根</param>
[SoloTaskInfo(TaskId, "自动七圣召唤", Group = "小游戏", Order = 510, Scopes = SoloTaskScopes.Standalone,
    WaitForMainUi = false)]
public class AutoGeniusInvokationTask(GameRuntime runtime, AllConfig config)
    : ISoloTask<GeniusInvokationTaskParam, NoData>
{
    /// <summary>
    /// 稳定任务标识
    /// </summary>
    public const string TaskId = "auto_genius_invokation";

    /// <summary>
    /// 运行自动七圣召唤。对战结束、策略指令执行完毕返回成功；没有可切换角色返回失败；取消时抛出异常
    /// </summary>
    /// <param name="param">本次运行的策略参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务结果</returns>
    public Task<SoloTaskResult<NoData>> RunAsync(GeniusInvokationTaskParam param, CancellationToken ct)
    {
        // 未选择策略时直接返回参数无效，不进入对局
        if (string.IsNullOrWhiteSpace(param.StrategyContent))
        {
            return Task.FromResult<SoloTaskResult<NoData>>(
                SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, "请先选择七圣召唤策略。"));
        }

        // 每次运行创建新的游戏操控对象，只操作本次的运行环境
        var control = new GeniusInvokationControl(runtime, config);

        // 读取策略信息；策略文本无法解析属于参数问题，返回失败结果
        Duel duel;
        try
        {
            duel = ScriptParser.Parse(runtime, config, control, param.StrategyContent);
        }
        catch (System.Exception e)
        {
            TaskControl.Logger.LogDebug(e, "解析七圣召唤策略失败");
            return Task.FromResult<SoloTaskResult<NoData>>(
                SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, $"七圣召唤策略解析失败：{e.Message}"));
        }

        // 对局是同步执行的；取消和其他异常直接向上抛出，由调用方处理
        duel.Run(ct);

        // 对局内部的提前结束（对战结束、指令执行完毕、没有可切换角色）由 Duel 记录结果
        return Task.FromResult<SoloTaskResult<NoData>>(duel.EndResult ?? SoloTaskResult.Unverified("自动七圣召唤结束"));
    }
}
