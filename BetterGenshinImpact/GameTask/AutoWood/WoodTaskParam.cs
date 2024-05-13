using System;
using System.Threading;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoWood;

public class WoodTaskParam : BaseTaskParam
{
    public int WoodRoundNum { get; set; }
    public int WoodDailyMaxCount { get; set; }

    public WoodTaskParam(CancellationTokenSource cts, int woodRoundNum, int woodDailyMaxCount) : base(cts)
    {
        WoodRoundNum = woodRoundNum;
        if (woodRoundNum == 0)
        {
            WoodRoundNum = 9999;
        }

        WoodDailyMaxCount = woodDailyMaxCount;
        if (WoodDailyMaxCount is 0 or >= 2000) WoodDailyMaxCount = 2000;
    }
}
