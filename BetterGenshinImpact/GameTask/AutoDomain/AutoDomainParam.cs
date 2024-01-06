using BetterGenshinImpact.GameTask.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainParam : BaseTaskParam
{

    public int DomainRoundNum { get; set; }

    public string? CombatStrategyContent { get; set; }

    public AutoDomainParam(CancellationTokenSource cts, int domainRoundNum) : base(cts)
    {
        DomainRoundNum = domainRoundNum;
        if (domainRoundNum == 0)
        {
            DomainRoundNum = 9999;
        }
    }
}