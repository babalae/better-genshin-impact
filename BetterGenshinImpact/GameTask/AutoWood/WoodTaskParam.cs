using System;
using System.Threading;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoWood;

public class WoodTaskParam : BaseTaskParam
{
    public int WoodRoundNum { get; set; }
    public TaskTriggerDispatcher Dispatcher { get; set; }

    public WoodTaskParam(CancellationTokenSource cts, TaskTriggerDispatcher dispatcher, int woodRoundNum) : base(cts)
    {
        Dispatcher = dispatcher;
        WoodRoundNum = woodRoundNum;
        if (woodRoundNum == 0)
        {
            WoodRoundNum = 9999;
        }
    }
}