using System.Collections.Generic;
using System.Linq;
using System;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;

namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>
/// 战斗策略工厂提供者
/// 根据已解析的策略类型选择工厂，不依赖文件扩展名或注册顺序
/// </summary>
public static class CombatTaskFactoryProvider
{
    /// <summary>固定注册的战斗任务工厂；类型集合必须互不重叠。</summary>
    private static readonly IReadOnlyList<ICombatTaskFactory> _factories =
    [
        // 固定注册全部实现，单一类型只匹配一个工厂。
        new ComboCombatTaskFactory(),
        new JsonCombatTaskFactory(),
        new TxtCombatTaskFactory(),
        new KeyMouseCombatTaskFactory(),
    ];

    /// <summary>
    /// 根据单一策略类型获取对应的工厂
    /// </summary>
    /// <param name="kind">解析后的单一策略类型</param>
    /// <returns>匹配的战斗任务工厂</returns>
    /// <exception cref="InvalidOperationException">不支持的文件类型</exception>
    public static ICombatTaskFactory GetFactory(CombatStrategyKind kind)
    {
        if (kind == CombatStrategyKind.None || ((int)kind & ((int)kind - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "必须指定单一战斗策略类型");
        var factory = _factories.SingleOrDefault(f => (f.Kinds & kind) != 0);
        if (factory == null)
        {
            throw new InvalidOperationException($"不支持的战斗策略类型：{kind}");
        }
        return factory;
    }
}
