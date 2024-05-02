using System.Threading;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class GeniusInvokationTaskParam(CancellationTokenSource cts, string strategyContent) : BaseTaskParam(cts)
{
    public string StrategyContent { get; set; } = strategyContent;
}
