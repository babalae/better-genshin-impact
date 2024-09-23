using BetterGenshinImpact.GameTask.Model;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class GeniusInvokationTaskParam(string strategyContent) : BaseTaskParam
{
    public string StrategyContent { get; set; } = strategyContent;
}
