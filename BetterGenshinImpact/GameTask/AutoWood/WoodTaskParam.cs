using BetterGenshinImpact.GameTask.Model;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoWood;

public class WoodTaskParam : BaseTaskParam<AutoWoodTask>
{
    public int WoodRoundNum { get; set; }
    public int WoodDailyMaxCount { get; set; }

    public WoodTaskParam(int woodRoundNum, int woodDailyMaxCount) : base(null, null)
    {
        WoodRoundNum = woodRoundNum;
        if (woodRoundNum == 0)
        {
            WoodRoundNum = 9999;
        }

        WoodDailyMaxCount = woodDailyMaxCount;
        if (WoodDailyMaxCount is 0 or >= 9999) WoodDailyMaxCount = 9999;
    }
}
