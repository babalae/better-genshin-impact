using System.IO;

namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>
/// .json 战斗策略工厂 — 创建 AutoFightJsonTask
/// </summary>
public class JsonCombatTaskFactory : ICombatTaskFactory
{
    public ISoloTask CreateTask(AutoFightParam param)
    {
        return new AutoFightJsonTask(param);
    }

    public bool CanHandle(string strategyPath)
    {
        return strategyPath.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase);
    }
}
