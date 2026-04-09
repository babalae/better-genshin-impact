using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy;

/// <summary>
/// Strategy for executing movement-based waypoint navigation tasks.
/// 执行基于移动的路点导航任务的策略。处理朝向、移动、接近以及相关动作触发的前后置逻辑。
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

        await BeforeMoveToTarget(executor, waypoint);
        
        if (string.Equals(waypoint.Type, WaypointType.Orientation.Code, StringComparison.OrdinalIgnoreCase))
        {
            await executor.MovementController.FaceTo(waypoint);
        }
        else if (!string.Equals(waypoint.Action, ActionEnum.UpDownGrabLeaf.Code, StringComparison.OrdinalIgnoreCase))
        {
            WaypointForTrack? previousWaypoint = null;
            if (executor.CurWaypoint.Item1 > 0)
            {
                var waypoints = executor.CurWaypoints.Item2;
                previousWaypoint = waypoints[executor.CurWaypoint.Item1 - 1];
            }
            await executor.MovementController.MoveTo(waypoint, previousWaypoint);
        }

        await BeforeMoveCloseToTarget(executor, waypoint);

        if (executor.IsTargetPoint(waypoint))
        {
            await executor.MovementController.MoveCloseTo(waypoint);
        }

        bool hasValidAction = !string.IsNullOrEmpty(waypoint.Action);
        bool shouldExecuteAction = (hasValidAction && !executor._navigator.SkipOtherOperations) ||
                                   string.Equals(waypoint.Action, ActionEnum.CombatScript.Code, StringComparison.OrdinalIgnoreCase);

        if (shouldExecuteAction)
        {
            AutoFightTask.FightWaypoint = string.Equals(waypoint.Action, ActionEnum.Fight.Code, StringComparison.OrdinalIgnoreCase) 
                ? waypoint 
                : null;

            await AfterMoveToTarget(executor, waypoint);
        }

        return false;
    }

    /// <summary>
    /// Executes pre-conditions or handlers right before closing the distance to the target.
    /// 在接近目标之前执行前置条件或处理器。例如处理飞行停止状态。
    /// </summary>
    /// <param name="executor">The current navigation scope instance. 当前导航作用域实例。</param>
    /// <param name="waypoint">The localized target location. 锚定的目标位置。</param>
    private async Task BeforeMoveCloseToTarget(PathExecutor executor, WaypointForTrack waypoint)
    {
        if (string.Equals(waypoint.MoveMode, MoveModeEnum.Fly.Code, StringComparison.OrdinalIgnoreCase) && 
            string.Equals(waypoint.Action, ActionEnum.StopFlying.Code, StringComparison.OrdinalIgnoreCase))
        {
            var handler = ActionFactory.GetBeforeHandler(ActionEnum.StopFlying.Code);
            if (handler != null)
            {
                await handler.RunAsync(executor.ct, waypoint);
            }
        }
    }

    /// <summary>
    /// Executes setup handlers before initiating target movement sequence.
    /// 在发起目标移动序列之前执行设置处理器。处理特殊动作如抓叶子、日志输出或通用前置动作。
    /// </summary>
    /// <param name="executor">The current navigation scope instance. 当前导航作用域实例。</param>
    /// <param name="waypoint">The localized target location. 锚定的目标位置。</param>
    private async Task BeforeMoveToTarget(PathExecutor executor, WaypointForTrack waypoint)
    {
        if (string.Equals(waypoint.Action, ActionEnum.UpDownGrabLeaf.Code, StringComparison.OrdinalIgnoreCase))
        {
            Simulation.SendInput.Mouse.MiddleButtonClick();
            await Delay(300, executor.ct);
            using (var screen = CaptureToRectArea())
            {
                if (screen?.SrcMat != null && !screen.SrcMat.IsDisposed)
                {
                    var position = await executor._navigator.GetPosition(screen, waypoint);
                    var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
                    await executor.WaitUntilRotatedTo(targetOrientation, 10);
                }
            }

            var handler = ActionFactory.GetBeforeHandler(waypoint.Action ?? string.Empty);
            if (handler != null)
            {
                await handler.RunAsync(executor.ct, waypoint);
            }
        }
        else if (string.Equals(waypoint.Action, ActionEnum.LogOutput.Code, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(waypoint.LogInfo))
            {
                Logger.LogInformation(waypoint.LogInfo);
            }
        }
        else
        {
            var handler = ActionFactory.GetBeforeHandler(waypoint.Action ?? string.Empty);
            if (handler != null)
            {
                await handler.RunAsync(executor.ct, waypoint);
            }
        }
    }

    /// <summary>
    /// Executes post-handlers after reaching the target, resolving specific tasks like combat or collection.
    /// 在抵达目标后执行后置处理器，解决特定任务，如战斗结束统计或收集状态轮询。
    /// </summary>
    /// <param name="executor">The current navigation scope instance. 当前导航作用域实例。</param>
    /// <param name="waypoint">The localized target location. 锚定的目标位置。</param>
    private async Task AfterMoveToTarget(PathExecutor executor, WaypointForTrack waypoint)
    {
        var handler = ActionFactory.GetAfterHandler(waypoint.Action ?? string.Empty);
        if (handler != null)
        {
            await handler.RunAsync(executor.ct, waypoint, executor.PartyConfig);
            
            // Increment combat counter if action type strictly matches combat
            // 统计结束战斗的次数
            if (string.Equals(waypoint.Action, ActionEnum.Fight.Code, StringComparison.OrdinalIgnoreCase))
            {
                executor.IncrementSuccessFight();
            }
            await Delay(1000, executor.ct);
        }
    }
}