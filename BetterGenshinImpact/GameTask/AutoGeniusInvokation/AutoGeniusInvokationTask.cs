using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class AutoGeniusInvokationTask(GeniusInvokationTaskParam taskParam) : ISoloTask
{
    public string Name => "自动七圣召唤";

    public Task Start(CancellationToken ct)
    {
        try
        {
            // 读取策略信息
            var duel = ScriptParser.Parse(taskParam.StrategyContent);
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