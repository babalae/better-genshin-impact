using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy;

/// <summary>
/// Defines the contract for executing specific waypoint navigation behaviors in mechanical contexts.
/// 定义执行特定路点导航行为的契约。为寻路引擎提供行为多态能力。
/// </summary>
public interface IWaypointStrategy
{
    /// <summary>
    /// Executes the navigation logic associated with a specific waypoint type asynchronously.
    /// 异步执行与特定路点类型关联的导航逻辑。
    /// </summary>
    /// <param name="executor">The path executor context managing the navigation lifecycle. 路径执行器上下文，管理导航生命周期。</param>
    /// <param name="waypoint">The target waypoint containing mechanical coordinate and action metadata. 目标路点，包含物理坐标与动作元数据。</param>
    /// <param name="waypointsList">The full topological waypoint routing dataset. 完整的拓扑路点路由数据集。</param>
    /// <returns>A boolean indicating whether the execution preempts further processing. 一个布尔值，表示执行操作是否抢占后续处理控制权。</returns>
    Task<bool> ExecuteAsync(PathExecutor executor, WaypointForTrack waypoint, List<List<WaypointForTrack>> waypointsList);
}
