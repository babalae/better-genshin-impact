using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

/// <summary>
/// 自动七圣召唤
/// </summary>
/// <param name="runtime">本次运行使用的运行环境</param>
/// <param name="config">配置根</param>
/// <param name="taskParam">任务参数</param>
public class AutoGeniusInvokationTask(GameRuntime runtime, AllConfig config, GeniusInvokationTaskParam taskParam) : ISoloTask
{
    public string Name => "自动七圣召唤";

    public Task Start(CancellationToken ct)
    {
        try
        {
            // 每次运行创建新的游戏操控对象，只操作本次的运行环境
            var control = new GeniusInvokationControl(runtime, config);
            // 读取策略信息
            var duel = ScriptParser.Parse(runtime, config, control, taskParam.StrategyContent);
            duel.Run(ct);
        }
        catch (System.Exception e)
        {
            TaskControl.Logger.LogDebug(e, "执行自动七圣召唤任务异常");
            TaskControl.Logger.LogError("执行自动七圣召唤任务异常：{Exception}", e.Message);
        }
        return Task.CompletedTask;
    }
}
