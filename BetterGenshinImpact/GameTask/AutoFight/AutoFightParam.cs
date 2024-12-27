using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoFight;

public class AutoFightParam(string path) : BaseTaskParam
{
    public string CombatStrategyPath { get; set; } = path;

    public bool FightFinishDetectEnabled { get; set; } = false;

    public bool PickDropsAfterFightEnabled { get; set; } = false;

    public int Timeout { get; set; } = 120;
}
