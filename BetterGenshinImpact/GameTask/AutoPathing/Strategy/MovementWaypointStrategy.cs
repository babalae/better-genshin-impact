using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy;

/// <summary>
/// 执行基于移动的路点导航任务的策略。处理朝向、移动、接近以及相关动作触发的前后置逻辑。
/// Strategy for executing movement-based waypoint navigation tasks.
/// </summary>
public class MovementWaypointStrategy : IWaypointStrategy
{
    /// <summary>
    /// Executes the movement waypoint navigation strategy.
    /// 执行移动路点导航策略。根据路点类型控制角色朝向或移动，并调度关联动作。
    /// </summary>
    /// <param name="executor">The path executor context managing the navigation lifecycle. 路径执行器上下文，管理导航生命周期。</param>
    /// <param name="waypoint">The target waypoint containing destination coordinates and actions. 目标路点，包含终点坐标与动作数据。</param>
    /// <param name="waypointsList">The full topological waypoint routing dataset. 完整的拓扑路点路由数据集。</param>
    /// <returns>Always false, indicating the movement block does not preempt the execution loop. 始终返回false，表示移动块不抢占执行循环。</returns>
    public async Task<bool> ExecuteAsync(PathExecutor executor, WaypointForTrack waypoint, List<List<WaypointForTrack>> waypointsList)
    {
        if (executor == null) throw new ArgumentNullException(nameof(executor));
        if (waypoint == null) throw new ArgumentNullException(nameof(waypoint));

        // 1. 前置动作（如：解包飞行、日志输出、抓取叶子等）
        await ExecuteHandlerAsync(ActionFactory.GetBeforeHandler, waypoint, executor);

        // 2. 将具体的移动方式委托给对应的行为策略
        await PerformLocomotionAsync(executor, waypoint);

        // 3. 接近点位的处理（如抵达指定坐标点前如果需要停飞或最后调整）
        await PerformProximityAsync(executor, waypoint);

        // 4. 后置动作及状态结算（如：重置战斗目标、收集状态等）
        await CompleteNavigationAsync(executor, waypoint);

        return false;
    }

    private async Task PerformLocomotionAsync(PathExecutor executor, WaypointForTrack waypoint)
    {
        if (string.Equals(waypoint.Type, WaypointType.Orientation.Code, StringComparison.OrdinalIgnoreCase))
        {
            await executor.MovementController.FaceTo(waypoint);
            return;
        }
        
        // 如果前置处理动作声明能够覆盖控制权（如由飞越四叶印自主解决移动），则完全跳过框架级别的位移
        var handler = ActionFactory.GetBeforeHandler(waypoint.Action ?? string.Empty);
        if (handler != null && handler.OverridesLocomotion)
        {
            return;
        }
        
        WaypointForTrack? previousWaypoint = null;
        WaypointForTrack? nextWaypoint = null;
        var waypoints = executor.CurWaypoints.Item2;
        var currentIndex = executor.CurWaypoint.Item1;
        
        if (currentIndex > 0)
        {
            previousWaypoint = waypoints[currentIndex - 1];
        }
        if (currentIndex < waypoints.Count - 1)
        {
            nextWaypoint = waypoints[currentIndex + 1];
        }
        
        await executor.MovementController.MoveTo(waypoint, previousWaypoint, nextWaypoint);
    }

    private async Task PerformProximityAsync(PathExecutor executor, WaypointForTrack waypoint)
    {
        // 检查某些在即将到达前才触发的条件，如即将靠岸/落地前停止飞行
        if (string.Equals(waypoint.MoveMode, MoveModeEnum.Fly.Code, StringComparison.OrdinalIgnoreCase) && 
            string.Equals(waypoint.Action, ActionEnum.StopFlying.Code, StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteHandlerAsync(ActionFactory.GetBeforeHandler, waypoint, executor);
        }

        if (executor.IsTargetPoint(waypoint))
        {
            await executor.MovementController.MoveCloseTo(waypoint);
        }
    }

    private async Task CompleteNavigationAsync(PathExecutor executor, WaypointForTrack waypoint)
    {
        bool hasValidAction = !string.IsNullOrEmpty(waypoint.Action);
        bool shouldExecuteAction = (hasValidAction && !executor._navigator.SkipOtherOperations) ||
                                   string.Equals(waypoint.Action, ActionEnum.CombatScript.Code, StringComparison.OrdinalIgnoreCase);

        if (shouldExecuteAction)
        {
            AutoFightTask.FightWaypoint = string.Equals(waypoint.Action, ActionEnum.Fight.Code, StringComparison.OrdinalIgnoreCase) 
                ? waypoint 
                : null;

            var handler = ActionFactory.GetAfterHandler(waypoint.Action ?? string.Empty);
            if (handler != null)
            {
                await handler.RunAsync(executor.ct, waypoint, executor.PartyConfig);
                
                // 特定后置统计：战斗次数
                if (string.Equals(waypoint.Action, ActionEnum.Fight.Code, StringComparison.OrdinalIgnoreCase))
                {
                    executor.IncrementSuccessFight();
                }
                await Delay(1000, executor.ct);
            }
        }
    }

    private async Task ExecuteHandlerAsync(Func<string, IActionHandler?> factoryMethod, WaypointForTrack waypoint, PathExecutor executor)
    {
        if (string.IsNullOrEmpty(waypoint.Action)) return;
        
        var handler = factoryMethod(waypoint.Action);
        if (handler != null)
        {
            // 通过 config 传递 executor 上下文，交给 handler 自己决策细节
            await handler.RunAsync(executor.ct, waypoint, executor);
        }
    }
}