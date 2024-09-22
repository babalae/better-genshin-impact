using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class AutoGeniusInvokationTask(GeniusInvokationTaskParam taskParam) : ISoloTask
{
    public Task Start()
    {
        // 读取策略信息
        var duel = ScriptParser.Parse(taskParam.StrategyContent);
        duel.Run(taskParam);
        return Task.CompletedTask;
    }
}
