using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.GameTask.AutoPathing.Suspend;

//暂停逻辑相关实现,这里主要用来记录，用来恢复相应操作
public class PathExecutorSuspend(PathExecutor pathExecutor) : ISuspendable
{
    private bool _isSuspended;

    private bool _resuming = false;
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
        _resuming = false;
    }

    //路径过远时，检查地图追踪点位经过暂停（当前点位和后一个点位算经过暂停），并重置状态
    public bool CheckAndResetSuspendPoint()
    {
        if (_isSuspended || !_resuming)
        {
            return false;
        }

        if (pathExecutor.CurWaypoints == default || pathExecutor.CurWaypoint == default)
        {
            Reset();
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
        //暂定恢复时，重置移动时的时间，防止因暂停而导致超时
        pathExecutor.moveToStartTime = DateTime.UtcNow;
        _isSuspended = false;
        _resuming = true;
    }

    public void Reset()
    {
        _resuming = false;
        _waypoints = default;
        _waypoint = default;
    }
}