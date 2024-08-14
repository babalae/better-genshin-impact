using BetterGenshinImpact.Helpers.Extensions;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class AutoGeniusInvokationTask
{
    public static void Start(GeniusInvokationTaskParam taskParam)
    {
        TaskTriggerDispatcher.Instance().StopTimer();
        // 读取策略信息
        var duel = ScriptParser.Parse(taskParam.StrategyContent);
        SystemControl.ActivateWindow();
        duel.RunAsync(taskParam).SafeForget();
    }
}