using System.IO;

namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>
/// .txt 战斗策略工厂 — 创建 AutoFightTask（旧版脚本）
/// </summary>
public class TxtCombatTaskFactory : ICombatTaskFactory
{
    public ISoloTask CreateTask(AutoFightParam param)
    {
        return new AutoFightTask(param);
    }

    public bool CanHandle(string strategyPath)
    {
        return string.IsNullOrEmpty(strategyPath)
            || Directory.Exists(strategyPath)
            || strategyPath.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase);
    }
}
