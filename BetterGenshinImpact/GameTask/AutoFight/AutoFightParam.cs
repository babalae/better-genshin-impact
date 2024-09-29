using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoFight;

public class AutoFightParam(string path) : BaseTaskParam
{
    public string CombatStrategyPath { get; set; } = path;

    public bool EndDetect { get; set; } = false;

    public bool AutoPickAfterFight { get; set; } = false;
}
