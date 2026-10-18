using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;
using System.IO;

namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>
/// .json 战斗策略工厂 — 创建 AutoFightJsonTask
/// </summary>
public class JsonCombatTaskFactory : ICombatTaskFactory
{
    public ISoloTask CreateTask(GameRuntime runtime, AllConfig config, AutoFightParam param)
    {
        return new AutoFightJsonTask(runtime, config, param);
    }

    public bool CanHandle(string strategyPath)
    {
        return strategyPath.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase);
    }
}
