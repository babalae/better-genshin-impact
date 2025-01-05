using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.GameTask.AutoPathing.Suspend;

//暂停逻辑相关实现,这里主要用来记录，用来恢复相应操作
public class PathExecutorSuspend(PathExecutor pathExecutor) : ISuspendable
{
    private bool _isSuspended;

    //记录当前相关点位数组
    private (int, List<WaypointForTrack>) _waypoints;

    //记录当前点位
    private (int, WaypointForTrack) _waypoint;

    public bool IsSuspended => _isSuspended;

    public void Suspend()
    {
        _waypoints = pathExecutor.CurWaypoints;
        _waypoint = pathExecutor.CurWaypoint;
        _isSuspended = true;
    }

    //路径过远时，检查路径追踪点位经过暂停（当前点位和后一个点位算经过暂停），并重置状态
    public bool CheckAndResetSuspendPoint()
    {
        if (_isSuspended)
        {
            return false;
        }

        if (pathExecutor.CurWaypoints == _waypoints && (pathExecutor.CurWaypoint == _waypoint || (pathExecutor.CurWaypoint.Item1 - 1) == _waypoint.Item1))
        {
            return true;
        }

        Reset();
        return false;
    }

    public void Resume()
    {
        _isSuspended = false;
    }

    public void Reset()
    {
        _waypoints = default;
        _waypoint = default;
    }
}