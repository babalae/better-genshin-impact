using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>键鼠录制战斗工厂，独立于同为 JSON 文件的条件策略。</summary>
public sealed class KeyMouseCombatTaskFactory : ICombatTaskFactory
{
    /// <summary>本工厂支持的策略类型。</summary>
    public CombatStrategyKind Kinds => CombatStrategyKind.KeyMouse;

    /// <summary>创建循环回放键鼠录制的战斗任务。</summary>
    public ISoloTask<AutoFightParam, NoData> CreateTask(GameRuntime runtime, AllConfig config)
        => new AutoFightKeyMouseTask(runtime, config);
}
