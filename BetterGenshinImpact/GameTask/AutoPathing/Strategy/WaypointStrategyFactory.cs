using System;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy;

/// <summary>
/// Static factory for resolving orthogonal waypoint navigation strategies dynamically.
/// 用于解析正交路点导航策略的静态工厂。消除了状态判断导致的硬编码冗余。
/// </summary>
public static class WaypointStrategyFactory
{
    /// <summary>
    /// Singleton instance of the active teleportation navigation strategy buffer.
    /// 传送导航策略的驻留单例实例。
    /// </summary>
    private static readonly TeleportWaypointStrategy _teleportStrategy = new();

    /// <summary>
    /// Singleton instance of the general movement navigation strategy buffer.
    /// 常规移动导航策略的驻留单例实例。
    /// </summary>
    private static readonly MovementWaypointStrategy _movementStrategy = new();

    /// <summary>
    /// Retrieves the appropriate structural navigation strategy mapping to a given waypoint behavior.
    /// 根据给定的路点类型映射获取对应的结构化导航策略。
    /// </summary>
    /// <param name="waypointTypeCode">The type code extracted from the destination node metadata. 从终点节点的元数据中提取的标识代码。</param>
    /// <returns>A resilient execution strategy bound by IWaypointStrategy. 受到 IWaypointStrategy 约束的稳健执行策略实例。</returns>
    public static IWaypointStrategy GetStrategy(string waypointTypeCode)
    {
        if (string.Equals(waypointTypeCode, WaypointType.Teleport.Code, StringComparison.OrdinalIgnoreCase))
        {
            return _teleportStrategy;
        }

        return _movementStrategy;
    }
}
