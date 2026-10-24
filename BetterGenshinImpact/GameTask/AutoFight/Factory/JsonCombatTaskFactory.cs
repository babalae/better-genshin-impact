using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using System.IO;

namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>
/// .json 战斗策略工厂 — 创建 AutoFightJsonTask
/// </summary>
public class JsonCombatTaskFactory : ICombatTaskFactory
{
    /// <summary>
    /// 创建 JSON 策略的自动战斗任务
    /// </summary>
    /// <param name="runtime">战斗任务所在的运行环境</param>
    /// <param name="config">配置根</param>
    /// <returns>JSON 策略的自动战斗任务</returns>
    public ISoloTask<AutoFightParam, NoData> CreateTask(GameRuntime runtime, AllConfig config)
    {
        return new AutoFightJsonTask(runtime, config);
    }

    /// <summary>本工厂支持的策略类型。</summary>
    public CombatStrategyKind Kinds => CombatStrategyKind.Json;
}
