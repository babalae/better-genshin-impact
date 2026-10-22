using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using System.IO;

namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>
/// .txt 战斗策略工厂 — 创建 AutoFightTask（旧版脚本）
/// </summary>
public class TxtCombatTaskFactory : ICombatTaskFactory
{
    /// <summary>
    /// 创建 txt 策略的自动战斗任务
    /// </summary>
    /// <param name="runtime">战斗任务所在的运行环境</param>
    /// <param name="config">配置根</param>
    /// <returns>txt 策略的自动战斗任务</returns>
    public ISoloTask<AutoFightParam, NoData> CreateTask(GameRuntime runtime, AllConfig config)
    {
        return new AutoFightTask(runtime, config);
    }

    /// <summary>本工厂支持的策略类型。</summary>
    public CombatStrategyKind Kinds => CombatStrategyKind.AutoSelect | CombatStrategyKind.Txt;
}
