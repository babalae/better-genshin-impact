using System;
using System.Threading;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoWood;

public class WoodTaskParam : BaseTaskParam
{
    public int WoodRoundNum { get; set; }

    public WoodTaskParam(CancellationTokenSource cts, int woodRoundNum) : base(cts)
    {
        WoodRoundNum = woodRoundNum;
        if (woodRoundNum == 0)
        {
            WoodRoundNum = 9999;
        }
    }
}
