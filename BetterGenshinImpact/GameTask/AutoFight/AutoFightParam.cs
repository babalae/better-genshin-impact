using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoFight;

public class AutoFightParam(string path) : BaseTaskParam
{
    public string CombatStrategyPath { get; set; } = path;
}
