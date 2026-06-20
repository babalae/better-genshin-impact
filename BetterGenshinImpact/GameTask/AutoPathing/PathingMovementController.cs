using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoPathing.Strategy.Movement;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathingMovementActions
{
    public Func<ImageRegion> CaptureAction { get; set; } = null!;
    public Func<ImageRegion, bool> EndJudgmentAction { get; set; } = null!;
    public Func<ImageRegion?, Task> ResolveAnomaliesAction { get; set; } = null!;
    public Func<int, int, Task> WaitUntilRotatedToAction { get; set; } = null!;
    public Func<string, Task<Avatar?>> SwitchAvatarAction { get; set; } = null!;
    public Func<Task> UseElementalSkillAction { get; set; } = null!;
    public Func<PathingPartyConfig> PartyConfigGetter { get; set; } = null!;
}

/// <summary>
/// 寻路移动控制器 / Pathing movement controller.
/// 控制角色在游戏内的寻路和移动逻辑 / Controls pathfinding and movement logic for the character in-game.
/// </summary>
public class PathingMovementController
{
    private const int TIMEOUT_SECONDS = 240;
    private const double ARRIVE_DISTANCE_THRESHOLD = 4.0;
    private const int MAX_PID_CONSECUTIVE_COUNT = 10;
    private const int MAX_STUCK_TRAP_COUNT = 2;
    private const int MAX_INERTIAL_RETRY_COUNT = 50;
    private const int NAVIGATION_BREAK_MINIMAP_GRACE_SECONDS = 15;
    private const float SMOOTH_RADIUS = 6.0f;

    private readonly CancellationToken _ct;
    private readonly PathingNavigator _navigator;
    private readonly CameraRotateTask _rotateTask;
    private readonly TrapEscaper _trapEscaper;
    private readonly Suspend.PathExecutorSuspend _pathExecutorSuspend;
    private readonly PathingMovementActions _actions;
    private DateTime _moveToStartTime;
    private DateTime _elementalSkillLastUseTime = DateTime.MinValue;
    private DateTime _useGadgetLastUseTime = DateTime.MinValue;
    private readonly Movement.StuckDetector _stuckDetector = new();
    private readonly Movement.InertialTracker _inertialTracker = new();
    private readonly List<IMoveModeHandler> _moveModeHandlers;
    private bool _positionContinuityInvalidated;

    /// <summary>
    /// 初始化寻路移动控制器 
    ///  Initializes a new instance of the pathing movement controller.
    /// </summary>
    public PathingMovementController(
        CancellationToken ct,
        PathingNavigator navigator,
        CameraRotateTask rotateTask,
        TrapEscaper trapEscaper,
        Suspend.PathExecutorSuspend pathExecutorSuspend,
        PathingMovementActions actions)
    {
        _ct = ct;
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _rotateTask = rotateTask ?? throw new ArgumentNullException(nameof(rotateTask));
        _trapEscaper = trapEscaper ?? throw new ArgumentNullException(nameof(trapEscaper));
        _pathExecutorSuspend = pathExecutorSuspend ?? throw new ArgumentNullException(nameof(pathExecutorSuspend));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        
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

    public void InvalidatePositionContinuity(string reason)
    {
        _positionContinuityInvalidated = true;
        Navigation.Reset();
        Logger.LogDebug("[寻路系统] 定位连续性已重置：{Reason}", reason);
    }

    /// <summary>
    /// 面向目标路径点 / Faces towards the target waypoint.
    /// </summary>
    /// <param name="waypoint">目标路径点 / Target waypoint.</param>
    /// <returns>异步任务结果 / Asynchronous task result.</returns>
    public async Task<bool> FaceTo(WaypointForTrack waypoint)
    {
        using var screen = _actions.CaptureAction();
        if (_actions.EndJudgmentAction(screen)) return true;

        var position = await _navigator.GetPosition(screen, waypoint);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogDebug("[寻路系统] 正在调整角色朝向，目标坐标：({X}, {Y})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        await _actions.WaitUntilRotatedToAction(targetOrientation, 2);
        await Delay(500, _ct);

        return false;
    }

    // 【自进化寻路】提供给上层调用的路线记录回调，无论有无偏差，只要成功走完，都记录下来用于生成提瓦特主干道。
    // 参数: (起步点, 终点, 真实摸索出的生还轨迹)
    public Action<WaypointForTrack?, WaypointForTrack, List<Point2f>, TimeSpan>? OnRouteTraversed { get; set; }

    public Action<WaypointForTrack?, WaypointForTrack, List<Point2f>, string>? OnRouteTraversalFailed { get; set; }

    /// <summary>
    /// 移动至指定的路径点 / Moves to the specified waypoint.
    /// </summary>
    /// <param name="waypoint">目标路径点 / Target waypoint.</param>
    /// <param name="previousWaypoint">上一个路径点 / Previous waypoint.</param>
    /// <returns>异步任务结果 / Asynchronous task result.</returns>
    public async Task<bool> MoveTo(WaypointForTrack waypoint, WaypointForTrack? previousWaypoint = null, WaypointForTrack? nextWaypoint = null)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        var partyConfig = _actions.PartyConfigGetter();
        await _actions.SwitchAvatarAction(partyConfig.MainAvatarIndex);

        var continuityInvalidated = ConsumePositionContinuityInvalidated();
        var validationPreviousWaypoint = continuityInvalidated ? null : previousWaypoint;
        if (continuityInvalidated)
        {
            Logger.LogInformation("[寻路系统] 上一次外部动作可能改变了位置或坐标域，本路径点将重新建立定位基准。");
        }

        var isNavigationBreak = PathingPositionValidator.IsNavigationBreak(validationPreviousWaypoint, waypoint);
        var navigationBreakRecovered = !isNavigationBreak;
        var navigationBreakStartTime = DateTime.UtcNow;
        if (isNavigationBreak)
        {
            var breakDistance = validationPreviousWaypoint == null
                ? 0
                : Navigation.GetDistance(validationPreviousWaypoint, new Point2f((float)waypoint.X, (float)waypoint.Y));
            Logger.LogInformation(
                "[寻路系统] 检测到非连续路线点（距离：{Distance:F1}），重置小地图匹配状态并等待过场后重新定位。",
                breakDistance);
            Navigation.Reset();
        }

        Point2f position = await InitialCoarseApproach(waypoint, validationPreviousWaypoint, isNavigationBreak || continuityInvalidated);
        if (isNavigationBreak && PathingPositionValidator.IsKnownPosition(position))
        {
            navigationBreakRecovered = true;
        }
        _moveToStartTime = DateTime.UtcNow;

        var moveContext = new PathingMovementContext
        {
            CancellationToken = _ct,
            PartyConfigGetter = () => partyConfig,
            UseElementalSkillAction = _actions.UseElementalSkillAction,
            GetElementalSkillLastUseTime = () => _elementalSkillLastUseTime,
            SetElementalSkillLastUseTime = t => _elementalSkillLastUseTime = t,
            GetUseGadgetLastUseTime = () => _useGadgetLastUseTime,
            SetUseGadgetLastUseTime = t => _useGadgetLastUseTime = t,
            FastMode = false,
            FastModeColdTime = DateTime.MinValue
        };

        _inertialTracker.Reset(position);
        _stuckDetector.Reset();
        _pidIntegral = 0f;
        _pidLastError = 0f;
        _pidLastTime = DateTime.UtcNow;

        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        
        using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        int consecutiveRotationCountBeyondAngle = 0;
        int additionalTimeInMs = 0;
        double distance = 0;
        bool isRouteCompleted = false;
        bool exitBecauseEndJudgment = false;
        bool reachedTargetPoint = false;
        Exception? caughtException = null;

        // 【自进化寻路】遥测采集数据
        var actualTrajectory = new List<Point2f>();
        var routeStartTime = DateTime.UtcNow;

        // 【架构颠覆: 组合式行为树 (Behavior Tree)】
        var rootNode = new BTSelector(
            
            // 致命拦截
            new BTSequence(
                new BTCondition(() => (DateTime.UtcNow - _moveToStartTime).TotalSeconds > TIMEOUT_SECONDS),
                new BTAction((Action)(() => {
                    Logger.LogWarning("[寻路系统] 路径点追踪超时（> {Timeout} 秒），已放弃当前路径追踪任务。", TIMEOUT_SECONDS);
                    isRouteCompleted = true;
                    exitBecauseEndJudgment = false;
                    caughtException = new RetryException("路径点执行超时，放弃整条路径");
                }))
            ),
            
            // 终点感知
            new BTSequence(
                new BTCondition(() => {
                    if (_actions.EndJudgmentAction(moveContext.Screen))
                    {
                        exitBecauseEndJudgment = true;
                        isRouteCompleted = true;
                        return true;
                    }
                    return false;
                }),
                new BTAction(() => BTStatus.Success)
            ),
            
            // 主推进与环境感知
            new BTSequence(
                new BTAction(() => MaintainForwardKey()),
                new BTAction(async () => {
                    try
                    {
                        var ignoreContinuityValidation = continuityInvalidated || (isNavigationBreak && !navigationBreakRecovered);
                        var (newPosition, addonTime) = await _navigator.GetPositionAndTime(moveContext.Screen, waypoint, validationPreviousWaypoint, ignoreContinuityValidation);
                        additionalTimeInMs = addonTime;
                        if (additionalTimeInMs > 0)
                        {
                            MaintainForwardKey();
                            additionalTimeInMs += 1000;
                        }

                        if (isNavigationBreak && !navigationBreakRecovered)
                        {
                            if (!PathingPositionValidator.IsKnownPosition(newPosition))
                            {
                                if ((DateTime.UtcNow - navigationBreakStartTime).TotalSeconds > NAVIGATION_BREAK_MINIMAP_GRACE_SECONDS)
                                {
                                    throw new RetryNoCountException("非连续路线点过场后小地图仍未恢复，重试一次此路线！");
                                }

                                if (moveContext.Num == 1 || moveContext.Num % 10 == 0)
                                {
                                    Logger.LogDebug("[寻路系统] 非连续路线点过场中，小地图暂不可用，继续等待重新定位。");
                                }

                                position = newPosition;
                                distance = double.PositiveInfinity;
                                return BTStatus.Running;
                            }

                            navigationBreakRecovered = true;
                            Navigation.SetPrevPosition(newPosition.X, newPosition.Y);
                            _inertialTracker.Reset(newPosition);
                            Logger.LogInformation("[寻路系统] 非连续路线点小地图已恢复，当前位置：({X:F1}, {Y:F1})。", newPosition.X, newPosition.Y);
                        }

                        if (continuityInvalidated && PathingPositionValidator.IsKnownPosition(newPosition))
                        {
                            continuityInvalidated = false;
                            Navigation.SetPrevPosition(newPosition.X, newPosition.Y);
                            _inertialTracker.Reset(newPosition);
                            Logger.LogInformation("[寻路系统] 已使用当前识别位置重建定位基准：({X:F1}, {Y:F1})。", newPosition.X, newPosition.Y);
                        }

                        position = await HandleInertialPositioning(waypoint, validationPreviousWaypoint, newPosition, moveContext.Screen, DateTime.UtcNow, ignoreContinuityValidation);
                        if (!PathingPositionValidator.IsKnownPosition(position))
                        {
                            distance = double.PositiveInfinity;
                            return BTStatus.Running;
                        }

                        distance = Navigation.GetDistance(waypoint, position);
                        //Logger.LogDebug("[寻路系统] 正在向目标点移动，当前实时距离：{Distance:F1}", distance);

                        // 【自进化寻路】定距抽样刻录真实坐标足迹 (每2.0距离记录一次)
                        if (PathingPositionValidator.IsKnownPosition(position))
                        {
                            var point2fPos = new Point2f(position.X, position.Y);
                            if (actualTrajectory.Count == 0 || Navigation.GetDistance(new Waypoint { X = actualTrajectory[^1].X, Y = actualTrajectory[^1].Y }, position) > 2.0)
                            {
                                actualTrajectory.Add(point2fPos);
                            }
                        }

                        return BTStatus.Success;
                    }
                    catch (Exception ex)
                    {
                        caughtException = ex;
                        isRouteCompleted = true;
                        exitBecauseEndJudgment = false;
                        return BTStatus.Failure;
                    }
                }),
                
                // 运动分支派布
                new BTSelector(
                    
                    // 到达内圈死区中断点
                    new BTSequence(
                        new BTCondition(() => distance < ARRIVE_DISTANCE_THRESHOLD),
                        new BTAction(() => {
                            // Logger.LogInformation("[寻路系统] 成功抵达目标路径点附近（距离 < {Threshold:F1}）。", ARRIVE_DISTANCE_THRESHOLD);
                            reachedTargetPoint = true;
                            isRouteCompleted = true;
                            return BTStatus.Success;
                        })
                    ),
                    
                    // 卡墙脱困反馈
                    new BTSequence(
                        new BTAction(async () => {
                            try 
                            {
                                bool needsEscape = await CheckAndHandleStuck(waypoint, validationPreviousWaypoint, position, additionalTimeInMs);
                                if (needsEscape) {
                                    // 触发脱困动作
                                    return BTStatus.Success;
                                }
                                return BTStatus.Failure;
                            }
                            catch (Exception ex)
                            {
                                caughtException = ex;
                                isRouteCompleted = true;
                                exitBecauseEndJudgment = false;
                                return BTStatus.Failure;
                            }
                        }),
                        new BTAction(() => {
                            consecutiveRotationCountBeyondAngle = 0;
                            return BTStatus.Running;
                        })
                    ),
                    
                    // 姿态驱动与按键分发
                    new BTSequence(
                        new BTAction(async () => {
                            consecutiveRotationCountBeyondAngle = await AlignOrientation(waypoint, position, moveContext.Screen, moveContext.Num, consecutiveRotationCountBeyondAngle, nextWaypoint);
                            moveContext.Distance = distance;
                            return BTStatus.Success;
                        }),
                        new BTAction(async () => {
                            var handlerStatus = await ApplyMoveModeHandlers(waypoint, moveContext);
                            if (handlerStatus == false)
                            {
                                // 中断链路，由于无法前进
                                isRouteCompleted = true;
                                exitBecauseEndJudgment = false;
                                return BTStatus.Failure;
                            }
                            AutoUseGadget(partyConfig);
                            return BTStatus.Running;
                        })
                    )
                )
            )
        );

        try
        {
            while (await ticker.WaitForNextTickAsync(_ct))
            {
                moveContext.Num++;
                using var screen = _actions.CaptureAction();
                moveContext.Screen = screen;
                
                await rootNode.TickAsync();
                
                // Clear the reference so we don't accidentally hold onto a disposed object in other branches
                moveContext.Screen = null!;
                
                if (caughtException != null)
                {
                    throw caughtException;
                }

                if (isRouteCompleted)
                {
                    if (reachedTargetPoint || exitBecauseEndJudgment)
                    {
                        // 【自进化寻路】完赛统计：抵达终点即刻作为提瓦特可行走主干道向外供出
                        if (OnRouteTraversed != null)
                        {
                            actualTrajectory.Add(new Point2f((float)waypoint.X, (float)waypoint.Y));
                            OnRouteTraversed.Invoke(previousWaypoint, waypoint, actualTrajectory, DateTime.UtcNow - routeStartTime);
                        }
                    }
                    else
                    {
                        OnRouteTraversalFailed?.Invoke(previousWaypoint, waypoint, actualTrajectory, "movement branch ended before reaching target");
                    }

                    return exitBecauseEndJudgment;
                }
            }
        }
        catch (Exception ex) when (ex is not TaskCanceledException and not OperationCanceledException)
        {
            OnRouteTraversalFailed?.Invoke(previousWaypoint, waypoint, actualTrajectory, $"{ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        }

        return false;
    }

    private bool ConsumePositionContinuityInvalidated()
    {
        var invalidated = _positionContinuityInvalidated;
        _positionContinuityInvalidated = false;
        return invalidated;
    }

    private void MaintainForwardKey()
    {
        if (!Simulation.IsKeyDown(GIActions.MoveForward.ToActionKey().ToVK()))
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        }
    }

    private async Task<Point2f> InitialCoarseApproach(WaypointForTrack waypoint, WaypointForTrack? previousWaypoint, bool isNavigationBreak)
    {
        using var screen = _actions.CaptureAction();
        var result = await _navigator.GetPositionAndTime(screen, waypoint, previousWaypoint, isNavigationBreak);
        var initialPosition = result.Item1;

        if (!PathingPositionValidator.IsKnownPosition(initialPosition) && previousWaypoint != null && !isNavigationBreak)
        {
            initialPosition = new Point2f((float)previousWaypoint.X, (float)previousWaypoint.Y);
            Logger.LogDebug(
                "[寻路系统] 初始小地图定位失败，使用上一点位作为初始基准坐标：({X:F1}, {Y:F1})",
                initialPosition.X,
                initialPosition.Y);
        }

        if (!PathingPositionValidator.IsKnownPosition(initialPosition))
        {
            if (isNavigationBreak)
            {
                Logger.LogDebug("[寻路系统] 非连续路线点初始定位失败，跳过初始转向，等待小地图恢复后再继续。");
            }

            return initialPosition;
        }

        var targetOrientation = Navigation.GetTargetOrientation(waypoint, initialPosition);
        Logger.LogDebug("[寻路系统] 启动初步接近，开始转向目标坐标：({X}, {Y})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        await _actions.WaitUntilRotatedToAction(targetOrientation, 5);
        return initialPosition;
    }

    private async Task<Point2f> HandleInertialPositioning(WaypointForTrack waypoint, WaypointForTrack? previousWaypoint, Point2f position, ImageRegion screen, DateTime now, bool ignoreContinuityValidation = false)
    {
        var validation = PathingPositionValidator.Validate(position, waypoint, previousWaypoint, _inertialTracker.LastValidPosition, ignoreContinuityValidation);

        if (validation.IsValid)
        {
            _inertialTracker.MarkValid(position, now);
            return position;
        }

        if (_pathExecutorSuspend.CheckAndResetSuspendPoint())
        {
            throw new RetryNoCountException("可能暂停导致定位异常，重试一次此路线！");
        }

        if (_inertialTracker.DistanceTooFarRetryCount > MAX_INERTIAL_RETRY_COUNT)
        {
            Logger.LogError(
                "[寻路系统] 视觉定位连续异常超过 {Count} 次，航迹推算已达极限（原因：{Reason}，目标距离：{TargetDistance:F1}，跳变距离：{JumpDistance:F1}，路线偏离：{SegmentDeviation:F1}），终止当前路径。请检查游戏画面或网络状态。",
                MAX_INERTIAL_RETRY_COUNT,
                validation.Reason,
                validation.TargetDistance,
                validation.JumpDistance,
                validation.SegmentDeviation);
            throw new HandledException("视觉定位持续异常，放弃此路径！");
        }

        if (_inertialTracker.DistanceTooFarRetryCount > 0 && _inertialTracker.DistanceTooFarRetryCount % 10 == 0)
        {
            await _actions.ResolveAnomaliesAction(screen);
            if (PathingPositionValidator.IsKnownPosition(_inertialTracker.LastValidPosition))
            {
                Logger.LogInformation("[寻路系统] 正在尝试通过异常处理重置推算基准，上次有效坐标：({X:F1}, {Y:F1})", _inertialTracker.LastValidPosition.X, _inertialTracker.LastValidPosition.Y);
                Navigation.SetPrevPosition(_inertialTracker.LastValidPosition.X, _inertialTracker.LastValidPosition.Y);
            }
            else
            {
                Logger.LogInformation("[寻路系统] 正在尝试通过异常处理恢复定位，当前还没有可用的有效坐标基准");
            }
            await Delay(500, _ct);
        }

        if (!PathingPositionValidator.IsKnownPosition(_inertialTracker.LastValidPosition))
        {
            return position;
        }

        position = _inertialTracker.TrackLost(now);
        
        if (_inertialTracker.DistanceTooFarRetryCount > 0 && _inertialTracker.DistanceTooFarRetryCount % 5 == 0)
        {
            Logger.LogWarning(
                "[寻路系统] 游戏视觉定位异常（原因：{Reason}，重试累计：{Count}次，目标距离：{TargetDistance:F1}，跳变距离：{JumpDistance:F1}，路线偏离：{SegmentDeviation:F1}），已切换至惯性航迹推算模式，当前预测位置：({X:F1}, {Y:F1})",
                validation.Reason,
                _inertialTracker.DistanceTooFarRetryCount,
                validation.TargetDistance,
                validation.JumpDistance,
                validation.SegmentDeviation,
                position.X,
                position.Y);
        }
        
        return position;
    }

    private async Task<bool> CheckAndHandleStuck(WaypointForTrack waypoint, WaypointForTrack? previousWaypoint, Point2f position, int additionalTimeInMs)
    {
         if (_inertialTracker.DistanceTooFarRetryCount > 0 || waypoint.MoveMode == MoveModeEnum.Climb.Code)
             return false;
             
        if (_stuckDetector.CheckStuck(position, additionalTimeInMs))
        {
            if (_stuckDetector.InTrapCount > MAX_STUCK_TRAP_COUNT)
            {
                throw new RetryException($"此路线出现{MAX_STUCK_TRAP_COUNT + 1}次卡死，重试一次路线或放弃此路线！");
            }

            Logger.LogWarning("[防卡死机制] 角色似乎遇到了障碍物，即将启动自动脱困逃脱程序...");
            await _trapEscaper.RotateAndMove();
            if (!await _trapEscaper.MoveTo(waypoint, previousWaypoint))
            {
                throw new RetryException("脱困失败，直接放弃！");
            }
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            Logger.LogInformation("[防卡死机制] 自动脱困完成，已恢复常规寻路。");
            
            _stuckDetector.ClearQueue();
            return true;
        }
        return false;
    }

    private float _pidIntegral = 0f;
    private float _pidLastError = 0f;
    private DateTime _pidLastTime = DateTime.MinValue;

    private Task<int> AlignOrientation(WaypointForTrack waypoint, Point2f position, ImageRegion screen, int loopNum, int consecutiveCount, WaypointForTrack? nextWaypoint = null)
    {
        int targetOrientation;
        var currentDistance = Navigation.GetDistance(waypoint, position);

        // 【平滑提升: 二阶贝塞尔(Bézier)柔性过弯预判】
        // 安全拦截：防止切角过大导致避障节点失效。只在无任何特殊动作，且处于常规地面移动状态下，并且缩小圆角半径至 6.0 内时才触发平滑。
        bool canSmooth = nextWaypoint != null 
            && string.IsNullOrEmpty(waypoint.Action) 
            && (waypoint.MoveMode == MoveModeEnum.Run.Code || waypoint.MoveMode == MoveModeEnum.Dash.Code || waypoint.MoveMode == MoveModeEnum.Walk.Code);

        if (canSmooth && currentDistance < SMOOTH_RADIUS && currentDistance > 2.0f)
        {
            // t 属于 0 到 1。进入 SMOOTH_RADIUS 单位缓冲圈后逐渐开始引导视线向下一个路口倾斜，实现小幅度丝滑拉弧角转弯
            float t = 1.0f - ((float)currentDistance / SMOOTH_RADIUS);
            t = Math.Clamp(t, 0f, 1f);
            
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            
            float targetX = uu * position.X + 2 * u * t * (float)waypoint.X + tt * (float)nextWaypoint!.X;
            float targetY = uu * position.Y + 2 * u * t * (float)waypoint.Y + tt * (float)nextWaypoint!.Y;
            
            var virtualWaypoint = new Waypoint { X = targetX, Y = targetY };
            targetOrientation = Navigation.GetTargetOrientation(virtualWaypoint, position);
        }
        else
        {
            targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        }

        var diff = _rotateTask.RotateToApproach(targetOrientation, screen);

        // 防御性保护：当小地图丢失、图像无法识别时，RotateToApproach 返回哨兵值 360f。
        // 若不将其剔除，它会作为 +360 的巨大误差值直接注入下方 PID 的积分与微分项中，
        // 瞬间触发 +-600 的满载补偿动作，导致死锁性质的剧烈抖动与瞎转。
        if (Math.Abs(diff) >= 360f)
        {
            consecutiveCount = 0;
            _pidIntegral = 0;
            return Task.FromResult(consecutiveCount);
        }
        
        // 动态转角容差：距离近时容差变大以防打圈，平时要求5度内
        int tolerance = currentDistance < 5.0 ? 15 : 5;

        // 计算真实的时间差 (dt)
        var now = DateTime.UtcNow;
        float dt = _pidLastTime != DateTime.MinValue ? (float)(now - _pidLastTime).TotalSeconds : 0.1f;
        if (dt <= 0f) dt = 0.01f; // 防止除0异常
        _pidLastTime = now;

        // 【平滑提升: 引入 PID 控制取代线程阻塞强行旋转】
        // 当偏差持续过大时，抛弃掉以往 await 一种固定帧率阻塞，而是叠加一抹带阻尼的高级鼠标偏移纠正
        if (loopNum > 20 && Math.Abs(diff) > tolerance)
        {
            consecutiveCount++;

            // 积分控制 (I): 如果多帧依然转不过来（比如鼠标卡住），累加动量
            _pidIntegral += diff * dt; // 采用真实的帧间差
            _pidIntegral = Math.Clamp(_pidIntegral, -60f, 60f); // 积分限幅防暴走
            
            // 微分控制 (D): 利用倒数前相预测震荡并衰减
            float derivative = (diff - _pidLastError) / dt;
            
            // PID 合成权重：这里补码作为额外动量输出，不抢占原初控制，只是在遇到强力折转/调头时增持辅助
            float pidCompensation = (0.5f * diff) + (0.2f * _pidIntegral) + (0.05f * derivative);

            if (consecutiveCount > MAX_PID_CONSECUTIVE_COUNT)
            {
                // 用 PID 算出应该补的像素。这种带有缓冲计算的动量比原本死锁等待的阻塞硬转柔和很多
                // 注意：RotateToApproach 计算出差值为正时，说明实际需要向左转（即负坐标偏移），因此这里叠加动量时必须反号以同向发力！
                int extraMouseDx = (int)Math.Clamp(-pidCompensation * 5.0f, -600, 600);
                if (extraMouseDx != 0)
                {
                    Simulation.SendInput?.Mouse?.MoveMouseBy(extraMouseDx, 0);
                }
                
                // 去除这里原先那种硬生生的堵塞 UI 线程的做法
                // 注释掉：await _actions.WaitUntilRotatedToAction(targetOrientation, 2);
            }
        }
        else
        {
            // 一旦追回，即刻抹除积分系历史，防止过调漂移回荡 (Overshoot)
            consecutiveCount = 0;
            _pidIntegral = 0;
        }

        _pidLastError = diff;
        return Task.FromResult(consecutiveCount);
    }

    private async Task<bool?> ApplyMoveModeHandlers(WaypointForTrack waypoint, PathingMovementContext context)
    {
        foreach (var handler in _moveModeHandlers)
        {
            if (handler.CanHandle(waypoint.MoveMode))
            {
                var handlerResult = await handler.ExecuteAsync(waypoint, context);
                if (handlerResult == MoveModeResult.ReturnFalse)
                {
                    return false; // Interrupt movement entirely
                }
                if (handlerResult == MoveModeResult.Continue)
                {
                    return true; // Bypass rest of the while loop body and continue to next iteration
                }
                break; // MoveModeResult.Pass falls through
            }
        }
        return null; // Proceed normal execution
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
        Logger.LogDebug("[寻路系统] 启动精确接近模式，目标坐标锁定在：({X}, {Y})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");

        const int maxSteps = 60;
        const float arriveDistance = 2f;
        const int maxLostRetryCount = 8;
        const int pulseForwardMs = 60;

        var stepsTaken = 0;
        var lostRetryCount = 0;
        Point2f? lastValidPosition = null;

        using var microTicker = new PeriodicTimer(TimeSpan.FromMilliseconds(20));

        ImageRegion screen = null!;
        Point2f position = default;
        double distance = 0f;
        bool isRouteCompleted = false;
        bool exitBecauseEndJudgment = false;

        var rootNode = new BTSelector(
            
            // 超时熔断
            new BTSequence(
                new BTCondition(() => stepsTaken > maxSteps),
                new BTAction((Action)(() => {
                    Logger.LogWarning("[精细寻路] 接近时间过长，已触发生命周期保护，当前微调接近中断。");
                    isRouteCompleted = true;
                }))
            ),

            // 目标判定达成
            new BTSequence(
                new BTCondition(() => {
                    if (_actions.EndJudgmentAction(screen))
                    {
                        exitBecauseEndJudgment = true;
                        isRouteCompleted = true;
                        return true;
                    }
                    return false;
                }),
                new BTAction(() => BTStatus.Success)
            ),

            // 精调管线主序列
            new BTSequence(
                // 视觉坐标采集与脱围容错判定
                new BTAction(async () => {
                    position = await _navigator.GetPosition(screen, waypoint);
                    var validation = PathingPositionValidator.Validate(position, waypoint, null, lastValidPosition);

                    if (!validation.IsValid)
                    {
                        lostRetryCount++;
                        if (lastValidPosition.HasValue && lostRetryCount <= maxLostRetryCount)
                        {
                            position = lastValidPosition.Value;
                            if (lostRetryCount == 1 || lostRetryCount % 3 == 0)
                                Logger.LogWarning(
                                    "[精细寻路] 瞬时视觉失常，启用位置缓存作为保险坐标：({X:F1}, {Y:F1})。原因：{Reason}，目标距离：{TargetDistance:F1}，跳变距离：{JumpDistance:F1}",
                                    position.X,
                                    position.Y,
                                    validation.Reason,
                                    validation.TargetDistance,
                                    validation.JumpDistance);
                            return BTStatus.Success; // 使用了兜底坐标，容许通过
                        }
                        
                        if (lostRetryCount % 3 == 0)
                            await _actions.ResolveAnomaliesAction(screen);

                        Logger.LogWarning(
                            "[精细寻路] 视觉坐标信号异常时间过长，安全停止本次路径微调。原因：{Reason}，目标距离：{TargetDistance:F1}，跳变距离：{JumpDistance:F1}",
                            validation.Reason,
                            validation.TargetDistance,
                            validation.JumpDistance);
                        isRouteCompleted = true;
                        return BTStatus.Failure;
                    }

                    if (lastValidPosition.HasValue)
                    {
                        var prevDistance = Navigation.GetDistance(waypoint, lastValidPosition.Value);
                        var currentDistance = Navigation.GetDistance(waypoint, position);
                        if (prevDistance < 6.0f && currentDistance > prevDistance + 0.3f)
                        {
                            Logger.LogDebug("[精细寻路] {Message}", "当前角色向后偏移或可能在进行徒劳寻圈，正在取消微调过程避免陷入死循环。");
                            isRouteCompleted = true;
                            return BTStatus.Failure;
                        }
                    }

                    lostRetryCount = 0;
                    lastValidPosition = position;
                    return BTStatus.Success;
                }),

                // 死区逼近检测与打断
                new BTSelector(
                    new BTSequence(
                        new BTCondition(() => {
                            distance = Navigation.GetDistance(waypoint, position);
                            return distance < arriveDistance;
                        }),
                        new BTAction((Action)(() => {
                            Logger.LogInformation("[精细寻路] 到达目标点（误差距离: < {Distance:F1}），此次寻路任务圆满完成。", arriveDistance);
                            isRouteCompleted = true;
                        }))
                    ),
                    
                    // 单发微位移控制 (Pulse Control)
                    new BTSequence(
                        new BTAction(async () => {
                            var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
                            await _actions.WaitUntilRotatedToAction(targetOrientation, distance < 3.5f ? 5 : 2);
                            
                            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                            try
                            {
                                await Delay(distance < 3.5f ? pulseForwardMs / 2 : pulseForwardMs, _ct);
                            }
                            finally
                            {
                                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                            }
                            
                            return BTStatus.Running; // 微位移循环结束，等待下一个帧
                        })
                    )
                )
            )
        );

        try
        {
            while (await microTicker.WaitForNextTickAsync(_ct))
            {
                stepsTaken++;
                using var currentScreen = _actions.CaptureAction();
                screen = currentScreen;

                await rootNode.TickAsync();

                screen = null!;

                if (isRouteCompleted)
                {
                    break; // 打断微循环计时流
                }
            }

            await Delay(1000, _ct);
            return exitBecauseEndJudgment;
        }
        finally
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        }
    }
}
