using System.Threading;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class GeniusInvokationTaskParam : BaseTaskParam
{
    public string StrategyContent { get; set; }

    public TaskTriggerDispatcher Dispatcher { get; set; }

    public GeniusInvokationTaskParam(CancellationTokenSource cts, TaskTriggerDispatcher dispatcher, string strategyContent) : base(cts)
    {
        StrategyContent = strategyContent;
        Dispatcher = dispatcher;
    }
}