using BetterGenshinImpact.GameTask.Model;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoFight;

public class AutoFightParam : BaseTaskParam
{
    public string CombatStrategyPath { get; set; }

    public AutoFightParam(CancellationTokenSource cts, string path) : base(cts)
    {
        CombatStrategyPath = path;
    }
}
