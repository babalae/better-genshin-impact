using BetterGenshinImpact.GameTask.Model;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainParam : BaseTaskParam
{

    public int DomainRoundNum { get; set; }

    public string CombatStrategyContent { get; set; }

    public AutoDomainParam(CancellationTokenSource cts, int domainRoundNum, string content) : base(cts)
    {
        DomainRoundNum = domainRoundNum;
        if (domainRoundNum == 0)
        {
            DomainRoundNum = 9999;
        }
        CombatStrategyContent = content;
    }
}