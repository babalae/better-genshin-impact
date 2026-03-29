using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Suspend;

//暂停逻辑相关实现,这里主要用来记录，用来恢复相应操作
public class PathExecutorSuspend(PathExecutor pathExecutor) : ISuspendable
{
    private const int ResumeRecoveryWindowMinSeconds = 30;
    private const int ResumeRecoveryWindowMaxSeconds = 300;
    private const double ResumeRecoveryWindowScale = 0.5;

    private bool _isSuspended;

    private bool _resuming = false;
    private DateTime _suspendTimeUtc = DateTime.MinValue;
    private DateTime _resumeTimeUtc = DateTime.MinValue;
    private TimeSpan _lastSuspendDuration = TimeSpan.Zero;
    private bool _resumeRetryConsumed;
    private int _lastRecoveryWindowSeconds;

    //记录当前相关点位数组
    private (int, List<WaypointForTrack>) _waypoints;

    //记录当前点位
    private (int, WaypointForTrack) _waypoint;

    public bool IsSuspended => _isSuspended;

    public void Suspend()
    {
        _waypoints = pathExecutor.CurWaypoints;
        _waypoint = pathExecutor.CurWaypoint;
        _suspendTimeUtc = DateTime.UtcNow;
        _isSuspended = true;
        _resuming = false;
        _resumeTimeUtc = DateTime.MinValue;
        _lastSuspendDuration = TimeSpan.Zero;
        _lastRecoveryWindowSeconds = 0;
        _resumeRetryConsumed = false;
        //暂停时记录，获取点位的暂停标志
        pathExecutor.GetPositionAndTimeSuspendFlag = true;
    }

    //路径过远时，检查地图追踪点位经过暂停（当前点位和后一个点位算经过暂停），并重置状态
    public bool CheckAndResetSuspendPoint()
    {
        if (_isSuspended || !_resuming)
        {
            return false;
        }

        // 只允许在恢复后的补偿窗口内触发一次特殊重试，避免重复触发导致死循环
        if (_resumeRetryConsumed || (DateTime.UtcNow - _resumeTimeUtc).TotalSeconds > _lastRecoveryWindowSeconds)
        {
            Logger.LogDebug("暂停恢复补偿窗口结束，恢复态重置（窗口={WindowSec}s）", _lastRecoveryWindowSeconds);
            Reset();
            return false;
        }

        if (pathExecutor.CurWaypoints == default || pathExecutor.CurWaypoint == default)
        {
            Reset();
            return false;
        }

        if (pathExecutor.CurWaypoints == _waypoints && (pathExecutor.CurWaypoint == _waypoint || (pathExecutor.CurWaypoint.Item1 - 1) == _waypoint.Item1))
        {
            _resumeRetryConsumed = true;
            Logger.LogWarning("命中暂停恢复补偿重试条件，触发一次免计数重试");
            Reset();
            return true;
        }

        // 未命中记录点位时继续保留恢复态，避免首轮判断失败就提前丢失补偿机会。
        return false;
    }

    public void Resume()
    {
        if (!_isSuspended)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_suspendTimeUtc != DateTime.MinValue)
        {
            _lastSuspendDuration = now - _suspendTimeUtc;
            if (_lastSuspendDuration < TimeSpan.Zero)
            {
                _lastSuspendDuration = TimeSpan.Zero;
            }
        }
        else
        {
            _lastSuspendDuration = TimeSpan.Zero;
        }

        _lastRecoveryWindowSeconds = GetResumeRecoveryWindowSeconds();
        Logger.LogInformation(
            "路径恢复：暂停时长={SuspendSec:F1}s，补偿窗口={WindowSec}s",
            _lastSuspendDuration.TotalSeconds,
            _lastRecoveryWindowSeconds);

        //暂定恢复时，重置移动时的时间，防止因暂停而导致超时
        pathExecutor.MovementController.ResetMoveToStartTime(now);
        _isSuspended = false;
        _resuming = true;
        _resumeRetryConsumed = false;
        _resumeTimeUtc = now;
        _suspendTimeUtc = DateTime.MinValue;
    }

    public void Reset()
    {
        _resuming = false;
        _resumeRetryConsumed = false;
        _resumeTimeUtc = DateTime.MinValue;
        _lastRecoveryWindowSeconds = 0;
        _waypoints = default;
        _waypoint = default;
    }

    private int GetResumeRecoveryWindowSeconds()
    {
        // 按暂停时长放大补偿窗口，避免长暂停后恢复时过早结束补偿。
        var dynamicWindow = ResumeRecoveryWindowMinSeconds + _lastSuspendDuration.TotalSeconds * ResumeRecoveryWindowScale;
        return (int)Math.Clamp(dynamicWindow, ResumeRecoveryWindowMinSeconds, ResumeRecoveryWindowMaxSeconds);
    }
}