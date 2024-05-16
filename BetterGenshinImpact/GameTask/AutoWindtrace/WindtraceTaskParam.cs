using System;
using System.Threading;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoWindtrace;

public class WindtraceTaskParam : BaseTaskParam
{
    public int CoinNum { get; set; }

    public WindtraceTaskParam(CancellationTokenSource cts, int coinNum) : base(cts)
    {
        CoinNum = coinNum;
    }
}
