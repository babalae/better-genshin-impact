using BetterGenshinImpact.GameTask.Model;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class GeniusInvokationTaskParam(CancellationTokenSource cts, string strategyContent) : BaseTaskParam(cts)
{
    public string StrategyContent { get; set; } = strategyContent;
}
