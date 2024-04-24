using BetterGenshinImpact.GameTask.Model;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainParam : BaseTaskParam
{
    public int DomainRoundNum { get; set; }

    public string CombatStrategyPath { get; set; }

    public AutoDomainParam(CancellationTokenSource cts, int domainRoundNum, string path) : base(cts)
    {
        DomainRoundNum = domainRoundNum;
        if (domainRoundNum == 0)
        {
            DomainRoundNum = 9999;
        }
        CombatStrategyPath = path;
    }
}
