using BetterGenshinImpact.GameTask.Model;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class GeniusInvokationTaskParam : BaseTaskParam<AutoGeniusInvokationTask>
{
    public GeniusInvokationTaskParam(string strategyContent) : base(null, null)
    {
        this.StrategyContent = strategyContent;
    }

    public string StrategyContent { get; set; }
}
