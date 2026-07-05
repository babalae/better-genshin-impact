using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>
/// 战斗策略工厂提供者
/// 根据策略文件扩展名自动选择合适的工厂
/// </summary>
public static class CombatTaskFactoryProvider
{
    private static readonly List<ICombatTaskFactory> _factories = [];

    static CombatTaskFactoryProvider()
    {
        // 注册顺序：json 优先，txt 兜底
        RegisterFactory(new JsonCombatTaskFactory());
        RegisterFactory(new TxtCombatTaskFactory());
    }

    /// <summary>
    /// 注册战斗任务工厂
    /// </summary>
    /// <param name="factory">战斗任务工厂实例</param>
    public static void RegisterFactory(ICombatTaskFactory factory)
    {
        _factories.Add(factory);
    }

    /// <summary>
    /// 根据策略文件路径获取对应的工厂
    /// </summary>
    /// <param name="strategyPath">策略文件路径</param>
    /// <returns>匹配的战斗任务工厂</returns>
    /// <exception cref="InvalidOperationException">不支持的文件类型</exception>
    public static ICombatTaskFactory GetFactory(string strategyPath)
    {
        strategyPath ??= string.Empty;
        var factory = _factories.FirstOrDefault(f => f.CanHandle(strategyPath));
        if (factory == null)
        {
            throw new System.InvalidOperationException($"不支持的战斗策略文件类型：{strategyPath}");
        }
        return factory;
    }
}
