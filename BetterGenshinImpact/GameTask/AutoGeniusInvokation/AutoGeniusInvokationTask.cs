using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class AutoGeniusInvokationTask(GeniusInvokationTaskParam taskParam) : ISoloTask
{
    public Task Start(CancellationTokenSource cts)
    {
        // 读取策略信息
        var duel = ScriptParser.Parse(taskParam.StrategyContent);
        duel.Run(cts);
        return Task.CompletedTask;
    }
}
