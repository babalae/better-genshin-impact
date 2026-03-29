using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy;

public class MovementWaypointStrategy : IWaypointStrategy
{
    public async Task<bool> ExecuteAsync(PathExecutor executor, WaypointForTrack waypoint, List<List<WaypointForTrack>> waypointsList)
    {
        await BeforeMoveToTarget(executor, waypoint);
        
        if (waypoint.Type == WaypointType.Orientation.Code)
        {
            await executor.MovementController.FaceTo(waypoint);
        }
        else if (waypoint.Action != ActionEnum.UpDownGrabLeaf.Code)
        {
            await executor.MovementController.MoveTo(waypoint);
        }

        await BeforeMoveCloseToTarget(executor, waypoint);

        if (executor.IsTargetPoint(waypoint))
        {
            await executor.MovementController.MoveCloseTo(waypoint);
        }

        if ((!string.IsNullOrEmpty(waypoint.Action) && !executor._navigator.SkipOtherOperations) ||
            waypoint.Action == ActionEnum.CombatScript.Code)
        {
            AutoFightTask.FightWaypoint = waypoint.Action == ActionEnum.Fight.Code ? waypoint : null;

            await AfterMoveToTarget(executor, waypoint);
        }

        return false;
    }

    private async Task BeforeMoveCloseToTarget(PathExecutor executor, WaypointForTrack waypoint)
    {
        if (waypoint.MoveMode == MoveModeEnum.Fly.Code && waypoint.Action == ActionEnum.StopFlying.Code)
        {
            var handler = ActionFactory.GetBeforeHandler(ActionEnum.StopFlying.Code);
            if (handler != null)
            {
                await handler.RunAsync(executor.ct, waypoint);
            }
        }
    }

    private async Task BeforeMoveToTarget(PathExecutor executor, WaypointForTrack waypoint)
    {
        if (waypoint.Action == ActionEnum.UpDownGrabLeaf.Code)
        {
            Simulation.SendInput.Mouse.MiddleButtonClick();
            await BetterGenshinImpact.GameTask.Common.TaskControl.Delay(300, executor.ct);
            var screen = BetterGenshinImpact.GameTask.Common.TaskControl.CaptureToRectArea();
            var position = await executor._navigator.GetPosition(screen, waypoint);
            var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            await executor.WaitUntilRotatedTo(targetOrientation, 10);
            var handler = ActionFactory.GetBeforeHandler(waypoint.Action ?? string.Empty);
            if (handler != null)
            {
                await handler.RunAsync(executor.ct, waypoint);
            }
        }
        else if (waypoint.Action == ActionEnum.LogOutput.Code)
        {
            Logger.LogInformation(waypoint.LogInfo);
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

    private async Task AfterMoveToTarget(PathExecutor executor, WaypointForTrack waypoint)
    {
        var handler = ActionFactory.GetAfterHandler(waypoint.Action ?? string.Empty);
        if (handler != null)
        {
            await handler.RunAsync(executor.ct, waypoint, executor.PartyConfig);
            //统计结束战斗的次数
            if (waypoint.Action == ActionEnum.Fight.Code)
            {
                executor.IncrementSuccessFight();
            }
            await BetterGenshinImpact.GameTask.Common.TaskControl.Delay(1000, executor.ct);
        }
    }
}