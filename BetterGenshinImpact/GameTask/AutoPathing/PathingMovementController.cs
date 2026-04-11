using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.AutoPathing.Strategy.Movement;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 寻路移动控制器 / Pathing movement controller.
/// 控制角色在游戏内的寻路和移动逻辑 / Controls pathfinding and movement logic for the character in-game.
/// </summary>
public class PathingMovementController
{
    private readonly CancellationToken _ct;
    private readonly PathingNavigator _navigator;
    private readonly CameraRotateTask _rotateTask;
    private readonly TrapEscaper _trapEscaper;
    private readonly Suspend.PathExecutorSuspend _pathExecutorSuspend;
    private readonly Func<ImageRegion> _captureAction;
    private readonly Func<ImageRegion, bool> _endJudgmentAction;
    private readonly Func<ImageRegion?, Task> _resolveAnomaliesAction;
    private readonly Func<int, int, Task> _waitUntilRotatedToAction;
    private readonly Func<string, Task<Avatar?>> _switchAvatarAction;
    private readonly Func<Task> _useElementalSkillAction;
    private readonly Func<PathingPartyConfig> _partyConfigGetter;
    private DateTime _moveToStartTime;
    private DateTime _elementalSkillLastUseTime = DateTime.MinValue;
    private DateTime _useGadgetLastUseTime = DateTime.MinValue;
    private readonly Movement.StuckDetector _stuckDetector = new();
    private readonly Movement.InertialTracker _inertialTracker = new();
    private readonly List<IMoveModeHandler> _moveModeHandlers;

    /// <summary>
    /// 初始化寻路移动控制器 / Initializes a new instance of the pathing movement controller.
    /// </summary>
    public PathingMovementController(
        CancellationToken ct,
        PathingNavigator navigator,
        CameraRotateTask rotateTask,
        TrapEscaper trapEscaper,
        Suspend.PathExecutorSuspend pathExecutorSuspend,
        Func<ImageRegion> captureAction,
        Func<ImageRegion, bool> endJudgmentAction,
        Func<ImageRegion?, Task> resolveAnomaliesAction,
        Func<int, int, Task> waitUntilRotatedToAction,
        Func<string, Task<Avatar?>> switchAvatarAction,
        Func<Task> useElementalSkillAction,
        Func<PathingPartyConfig> partyConfigGetter)
    {
        _ct = ct;
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _rotateTask = rotateTask ?? throw new ArgumentNullException(nameof(rotateTask));
        _trapEscaper = trapEscaper ?? throw new ArgumentNullException(nameof(trapEscaper));
        _pathExecutorSuspend = pathExecutorSuspend ?? throw new ArgumentNullException(nameof(pathExecutorSuspend));
        _captureAction = captureAction ?? throw new ArgumentNullException(nameof(captureAction));
        _endJudgmentAction = endJudgmentAction ?? throw new ArgumentNullException(nameof(endJudgmentAction));
        _resolveAnomaliesAction = resolveAnomaliesAction ?? throw new ArgumentNullException(nameof(resolveAnomaliesAction));
        _waitUntilRotatedToAction = waitUntilRotatedToAction ?? throw new ArgumentNullException(nameof(waitUntilRotatedToAction));
        _switchAvatarAction = switchAvatarAction ?? throw new ArgumentNullException(nameof(switchAvatarAction));
        _useElementalSkillAction = useElementalSkillAction ?? throw new ArgumentNullException(nameof(useElementalSkillAction));
        _partyConfigGetter = partyConfigGetter ?? throw new ArgumentNullException(nameof(partyConfigGetter));
        
        _moveModeHandlers = new List<IMoveModeHandler>()
        {
            new FlyMoveModeHandler(),
            new JumpMoveModeHandler(),
            new DashMoveModeHandler(),
            new RunMoveModeHandler(),
            new ClimbMoveModeHandler(),
            new DefaultMoveModeHandler()
        };
    }

    /// <summary>
    /// 重置移动开始时间 / Resets move start time.
    /// </summary>
    /// <param name="time">时间 / Time.</param>
    public void ResetMoveToStartTime(DateTime time)
    {
        _moveToStartTime = time;
    }

    /// <summary>
    /// 面向目标路径点 / Faces towards the target waypoint.
    /// </summary>
    /// <param name="waypoint">目标路径点 / Target waypoint.</param>
    /// <returns>异步任务结果 / Asynchronous task result.</returns>
    public async Task<bool> FaceTo(WaypointForTrack waypoint)
    {
        using var screen = _captureAction();
        if (_endJudgmentAction(screen)) return true;

        var position = await _navigator.GetPosition(screen, waypoint);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogDebug("朝向点，位置({x2},{y2})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        await _waitUntilRotatedToAction(targetOrientation, 2);
        await Delay(500, _ct);

        return false;
    }

    /// <summary>
    /// 移动至指定的路径点 / Moves to the specified waypoint.
    /// </summary>
    /// <param name="waypoint">目标路径点 / Target waypoint.</param>
    /// <param name="previousWaypoint">上一个路径点 / Previous waypoint.</param>
    /// <returns>异步任务结果 / Asynchronous task result.</returns>
    public async Task<bool> MoveTo(WaypointForTrack waypoint, WaypointForTrack? previousWaypoint = null)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        var partyConfig = _partyConfigGetter();
        await _switchAvatarAction(partyConfig.MainAvatarIndex);

        Point2f position = await InitialCoarseApproach(waypoint);
        _moveToStartTime = DateTime.UtcNow;

        var fastMode = false;
        var fastModeColdTime = DateTime.MinValue;
        
        _inertialTracker.Reset(position);
        _stuckDetector.Reset();

        int num = 0, consecutiveRotationCountBeyondAngle = 0;

        var moveContext = new PathingMovementContext
        {
            CancellationToken = _ct,
            PartyConfigGetter = () => partyConfig,
            UseElementalSkillAction = _useElementalSkillAction,
            GetElementalSkillLastUseTime = () => _elementalSkillLastUseTime,
            SetElementalSkillLastUseTime = t => _elementalSkillLastUseTime = t,
            GetUseGadgetLastUseTime = () => _useGadgetLastUseTime,
            SetUseGadgetLastUseTime = t => _useGadgetLastUseTime = t,
            FastMode = fastMode,
            FastModeColdTime = fastModeColdTime
        };

        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        
        try
        {
            while (!_ct.IsCancellationRequested)
            {
                MaintainForwardKey();
                
                num++;
                var now = DateTime.UtcNow;
                if ((now - _moveToStartTime).TotalSeconds > 240)
                {
                    Logger.LogWarning("执行超时，放弃此次追踪");
                    throw new RetryException("路径点执行超时，放弃整条路径");
                }

                using var screen = _captureAction();
                if (_endJudgmentAction(screen))
                {
                    return true;
                }

                var (newPosition, additionalTimeInMs) = await _navigator.GetPositionAndTime(screen, waypoint);
                position = newPosition;
                
                if (additionalTimeInMs > 0)
                {
                    MaintainForwardKey();
                    additionalTimeInMs += 1000;
                }

                position = await HandleInertialPositioning(waypoint, position, screen, now);
                var distance = Navigation.GetDistance(waypoint, position);
                Debug.WriteLine($"接近目标点中，距离为{distance}");
                
                if (distance < 4)
                {
                    Logger.LogDebug("到达路径点附近");
                    break;
                }

                await CheckAndHandleStuck(waypoint, previousWaypoint, position, additionalTimeInMs);

                consecutiveRotationCountBeyondAngle = await AlignOrientation(waypoint, position, screen, num, consecutiveRotationCountBeyondAngle);

                moveContext.Screen = screen;
                moveContext.Num = num;
                moveContext.Distance = distance;

                if (await ApplyMoveModeHandlers(waypoint, moveContext))
                {
                    // Movement completed by a handler completely (ReturnFalse equivalent)
                    return false;
                }

                fastModeColdTime = moveContext.FastModeColdTime;
                fastMode = moveContext.FastMode;

                AutoUseGadget(partyConfig);

                await Delay(100, _ct);
            }
        }
        finally
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        }

        return false;
    }

    private void MaintainForwardKey()
    {
        if (!Simulation.IsKeyDown(GIActions.MoveForward.ToActionKey().ToVK()))
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        }
    }

    private async Task<Point2f> InitialCoarseApproach(WaypointForTrack waypoint)
    {
        using var screen = _captureAction();
        var result = await _navigator.GetPositionAndTime(screen, waypoint);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, result.Item1);
        Logger.LogDebug("粗略接近途经点，位置({x2},{y2})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        await _waitUntilRotatedToAction(targetOrientation, 5);
        return result.Item1;
    }

    private async Task<Point2f> HandleInertialPositioning(WaypointForTrack waypoint, Point2f position, ImageRegion screen, DateTime now)
    {
        var rawDistance = Navigation.GetDistance(waypoint, position);
        var isPositionLost = (position.X == 0 && position.Y == 0) || rawDistance > 500;

        if (!isPositionLost)
        {
            _inertialTracker.MarkValid(position, now);
            return position;
        }

        if (_pathExecutorSuspend.CheckAndResetSuspendPoint())
        {
            throw new RetryNoCountException("可能暂停导致路径过远，重试一次此路线！");
        }

        if (_inertialTracker.DistanceTooFarRetryCount > 50)
        {
            Logger.LogWarning($"定位连续丢失 50 次，航迹推算达到极限。距离: {rawDistance}，放弃此路径点！");
            throw new HandledException("目标距离过远或定位彻底丢失，放弃此路径！");
        }

        if (_inertialTracker.DistanceTooFarRetryCount > 0 && _inertialTracker.DistanceTooFarRetryCount % 10 == 0)
        {
            await _resolveAnomaliesAction(screen);
            Logger.LogInformation($"重置到上次正确识别的推算基准坐标 ({_inertialTracker.LastValidPosition.X},{_inertialTracker.LastValidPosition.Y})");
            Navigation.SetPrevPosition(_inertialTracker.LastValidPosition.X, _inertialTracker.LastValidPosition.Y);
            await Delay(500, _ct);
        }
        
        position = _inertialTracker.TrackLost(now);
        
        if (_inertialTracker.DistanceTooFarRetryCount > 0 && _inertialTracker.DistanceTooFarRetryCount % 5 == 0)
        {
            Logger.LogWarning($"视觉定位丢失 (重试 {_inertialTracker.DistanceTooFarRetryCount})，启用强退化惯性航迹推算。预测坐标：({position.X:F1},{position.Y:F1})");
        }
        
        return position;
    }

    private async Task CheckAndHandleStuck(WaypointForTrack waypoint, WaypointForTrack? previousWaypoint, Point2f position, int additionalTimeInMs)
    {
         if (_inertialTracker.DistanceTooFarRetryCount > 0 || waypoint.MoveMode == MoveModeEnum.Climb.Code)
             return;
             
        if (_stuckDetector.CheckStuck(position, additionalTimeInMs))
        {
            if (_stuckDetector.InTrapCount > 2)
            {
                throw new RetryException("此路线出现3次卡死，重试一次路线或放弃此路线！");
            }

            Logger.LogWarning("疑似卡死，尝试脱离...");
            await _trapEscaper.RotateAndMove();
            if (!await _trapEscaper.MoveTo(waypoint, previousWaypoint))
            {
                throw new RetryException("脱困失败，直接放弃！");
            }
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            Logger.LogInformation("卡死脱离结束");
            
            _stuckDetector.ClearQueue();
        }
    }

    private async Task<int> AlignOrientation(WaypointForTrack waypoint, Point2f position, ImageRegion screen, int loopNum, int consecutiveCount)
    {
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        var diff = _rotateTask.RotateToApproach(targetOrientation, screen);
        if (loopNum > 20)
        {
            consecutiveCount = Math.Abs(diff) > 5 ? consecutiveCount + 1 : 0;
            if (consecutiveCount > 10)
            {
                await _waitUntilRotatedToAction(targetOrientation, 2);
                consecutiveCount = 0;
            }
        }
        return consecutiveCount;
    }

    private async Task<bool> ApplyMoveModeHandlers(WaypointForTrack waypoint, PathingMovementContext context)
    {
        foreach (var handler in _moveModeHandlers)
        {
            if (handler.CanHandle(waypoint.MoveMode))
            {
                var handlerResult = await handler.ExecuteAsync(waypoint, context);
                if (handlerResult == MoveModeResult.ReturnFalse)
                {
                    return true;
                }
                break;
            }
        }
        return false;
    }

    private void AutoUseGadget(PathingPartyConfig partyConfig)
    {
        if (partyConfig.UseGadgetIntervalMs > 0 && (DateTime.UtcNow - _useGadgetLastUseTime).TotalMilliseconds > partyConfig.UseGadgetIntervalMs)
        {
            Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
            _useGadgetLastUseTime = DateTime.UtcNow;
        }
    }


    /// <summary>
    /// 精确接近指定的路径点。
    /// </summary>
    /// <param name="waypoint">目标路径点。</param>
    /// <returns>到达目的地返回 true，否则返回 false。</returns>
    public async Task<bool> MoveCloseTo(WaypointForTrack waypoint)
    {
        Logger.LogDebug("精确接近目标点，位置({x2},{y2})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");

        const int maxSteps = 25;
        const float arriveDistance = 2f;
        const int maxLostRetryCount = 8;
        const int pulseForwardMs = 60;

        var stepsTaken = 0;
        var lostRetryCount = 0;
        Point2f? lastValidPosition = null;

        while (!_ct.IsCancellationRequested)
        {
            stepsTaken++;
            if (stepsTaken > maxSteps)
            {
                Logger.LogWarning("精确接近超时");
                break;
            }

            using var screen = _captureAction();
            if (_endJudgmentAction(screen))
            {
                return true;
            }

            var position = await _navigator.GetPosition(screen, waypoint);
            var rawDistance = Navigation.GetDistance(waypoint, position);
            var isPositionLost = (position.X == 0 && position.Y == 0) || rawDistance > 500;

            if (isPositionLost)
            {
                lostRetryCount++;
                if (lastValidPosition.HasValue && lostRetryCount <= maxLostRetryCount)
                {
                    position = lastValidPosition.Value;
                    if (lostRetryCount == 1 || lostRetryCount % 3 == 0)
                    {
                        Logger.LogWarning($"精确接近阶段定位丢失，使用最近有效坐标兜底（第 {lostRetryCount} 次）：({position.X:F1},{position.Y:F1})");
                    }
                }
                else
                {
                    if (lostRetryCount % 3 == 0)
                    {
                        await _resolveAnomaliesAction(screen);
                    }

                    Logger.LogWarning("精确接近阶段定位持续丢失，终止本次精确接近");
                    break;
                }
            }
            else
            {
                if (lastValidPosition.HasValue)
                {
                    var prevDistance = Navigation.GetDistance(waypoint, lastValidPosition.Value);
                    var currentDistance = Navigation.GetDistance(waypoint, position);

                    // 防绕圈/防越过机制：如果距离较近，且发生明显的距离反增（超过容差0.3f）
                    if (prevDistance < 6.0f && currentDistance > prevDistance + 0.3f)
                    {
                        Logger.LogDebug("检测到距离不减反增(可能由于步长过大越过目标点或绕圈)，提前终止精确接近: {Prev} -> {Cur}", prevDistance, currentDistance);
                        break;
                    }
                }

                lostRetryCount = 0;
                lastValidPosition = position;
            }

            var distance = Navigation.GetDistance(waypoint, position);
            if (distance < arriveDistance)
            {
                Logger.LogDebug("已到达路径点");
                break;
            }

            var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            // 极近距离下，稍微放宽对准角度容差以防止准星高频抖动进入轨道路线
            await _waitUntilRotatedToAction(targetOrientation, distance < 3.5f ? 5 : 2);
            
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            // 距离越近，点按时长减半（例如把 60ms 切成 30ms），极大降低飞跑越过目标的概率
            await Delay(distance < 3.5f ? pulseForwardMs / 2 : pulseForwardMs, _ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            await Delay(20, _ct);
        }

        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        await Delay(1000, _ct);
        return false;
    }
}
