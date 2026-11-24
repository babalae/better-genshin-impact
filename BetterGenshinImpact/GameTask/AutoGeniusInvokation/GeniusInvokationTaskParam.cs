using BetterGenshinImpact.GameTask.Model;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class GeniusInvokationTaskParam : BaseTaskParam
{
    public GeniusInvokationTaskParam(string strategyContent) : base(null)
    {
        this.StrategyContent = strategyContent;
    }

    public string StrategyContent { get; set; }
}
