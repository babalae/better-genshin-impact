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
    private const int TIMEOUT_SECONDS = 240;
    private const double ARRIVE_DISTANCE_THRESHOLD = 4.0;
    private const int MAX_PID_CONSECUTIVE_COUNT = 10;
    private const int MAX_STUCK_TRAP_COUNT = 2;
    private const int MAX_INERTIAL_RETRY_COUNT = 50;
    private const float SMOOTH_RADIUS = 6.0f;

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
    /// 初始化寻路移动控制器 
    ///  Initializes a new instance of the pathing movement controller.
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
        Logger.LogDebug("[寻路系统] 正在调整角色朝向，目标坐标：({X}, {Y})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        await _waitUntilRotatedToAction(targetOrientation, 2);
        await Delay(500, _ct);

        return false;
    }

    // 【自进化寻路】提供给上层调用的路线记录回调，无论有无偏差，只要成功走完，都记录下来用于生成提瓦特主干道。
    // 参数: (起步点, 终点, 真实摸索出的生还轨迹)
    public Action<WaypointForTrack?, WaypointForTrack, List<Point2f>>? OnRouteTraversed { get; set; }

    /// <summary>
    /// 移动至指定的路径点 / Moves to the specified waypoint.
    /// </summary>
    /// <param name="waypoint">目标路径点 / Target waypoint.</param>
    /// <param name="previousWaypoint">上一个路径点 / Previous waypoint.</param>
    /// <returns>异步任务结果 / Asynchronous task result.</returns>
    public async Task<bool> MoveTo(WaypointForTrack waypoint, WaypointForTrack? previousWaypoint = null, WaypointForTrack? nextWaypoint = null)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        var partyConfig = _partyConfigGetter();
        await _switchAvatarAction(partyConfig.MainAvatarIndex);

        Point2f position = await InitialCoarseApproach(waypoint);
        _moveToStartTime = DateTime.UtcNow;

        var moveContext = new PathingMovementContext
        {
            CancellationToken = _ct,
            PartyConfigGetter = () => partyConfig,
            UseElementalSkillAction = _useElementalSkillAction,
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
        Exception? caughtException = null;

        // 【自进化寻路】遥测采集数据
        var actualTrajectory = new List<Point2f>();

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
                    if (_endJudgmentAction(moveContext.Screen))
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
                        var (newPosition, addonTime) = await _navigator.GetPositionAndTime(moveContext.Screen, waypoint);
                        additionalTimeInMs = addonTime;
                        if (additionalTimeInMs > 0)
                        {
                            MaintainForwardKey();
                            additionalTimeInMs += 1000;
                        }
                        position = await HandleInertialPositioning(waypoint, newPosition, moveContext.Screen, DateTime.UtcNow);
                        distance = Navigation.GetDistance(waypoint, position);
                        Logger.LogDebug("[寻路系统] 正在向目标点移动，当前实时距离：{Distance:F1}", distance);

                        // 【自进化寻路】定距抽样刻录真实坐标足迹 (每2.0距离记录一次)
                        if (position.X != 0 && position.Y != 0)
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
                            Logger.LogInformation("[寻路系统] 成功抵达目标路径点附近（距离 < {Threshold:F1}）。", ARRIVE_DISTANCE_THRESHOLD);
                            isRouteCompleted = true;
                            return BTStatus.Success;
                        })
                    ),
                    
                    // 卡墙脱困反馈
                    new BTSequence(
                        new BTAction(async () => {
                            try 
                            {
                                bool needsEscape = await CheckAndHandleStuck(waypoint, previousWaypoint, position, additionalTimeInMs);
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
                using var screen = _captureAction();
                moveContext.Screen = screen;
                
                await rootNode.TickAsync();
                
                if (caughtException != null)
                {
                    throw caughtException;
                }

                if (isRouteCompleted)
                {
                    // 【自进化寻路】完赛统计：抵达终点即刻作为提瓦特可行走主干道向外供出
                    if (OnRouteTraversed != null)
                    {
                        actualTrajectory.Add(new Point2f((float)waypoint.X, (float)waypoint.Y)); 
                        OnRouteTraversed.Invoke(previousWaypoint, waypoint, actualTrajectory);
                    }

                    return exitBecauseEndJudgment;
                }
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
        Logger.LogDebug("[寻路系统] 启动初步接近，开始转向目标坐标：({X}, {Y})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
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
            Logger.LogError("[寻路系统] 视觉定位连续丢失超过 50 次，航迹推算已达极限（偏离距离：{Distance:F1}），终止当前路径。请检查游戏画面或网络状态。", rawDistance);
            throw new HandledException("目标距离过远或定位彻底丢失，放弃此路径！");
        }

        if (_inertialTracker.DistanceTooFarRetryCount > 0 && _inertialTracker.DistanceTooFarRetryCount % 10 == 0)
        {
            await _resolveAnomaliesAction(screen);
            Logger.LogInformation("[寻路系统] 正在尝试通过异常处理重置推算基准，上次有效坐标：({X:F1}, {Y:F1})", _inertialTracker.LastValidPosition.X, _inertialTracker.LastValidPosition.Y);
            Navigation.SetPrevPosition(_inertialTracker.LastValidPosition.X, _inertialTracker.LastValidPosition.Y);
            await Delay(500, _ct);
        }
        
        position = _inertialTracker.TrackLost(now);
        
        if (_inertialTracker.DistanceTooFarRetryCount > 0 && _inertialTracker.DistanceTooFarRetryCount % 5 == 0)
        {
            Logger.LogWarning("[寻路系统] 游戏视觉定位丢失（重试累计：{Count}次），已切换至惯性航迹推算模式，当前预测位置：({X:F1}, {Y:F1})", _inertialTracker.DistanceTooFarRetryCount, position.X, position.Y);
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

    private async Task<int> AlignOrientation(WaypointForTrack waypoint, Point2f position, ImageRegion screen, int loopNum, int consecutiveCount, WaypointForTrack? nextWaypoint = null)
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
            
            float targetX = uu * position.X + 2 * u * t * (float)waypoint.X + tt * (float)nextWaypoint.X;
            float targetY = uu * position.Y + 2 * u * t * (float)waypoint.Y + tt * (float)nextWaypoint.Y;
            
            var virtualWaypoint = new Waypoint { X = targetX, Y = targetY };
            targetOrientation = Navigation.GetTargetOrientation(virtualWaypoint, position);
        }
        else
        {
            targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        }

        var diff = _rotateTask.RotateToApproach(targetOrientation, screen);
        
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
                int extraMouseDx = (int)Math.Clamp(pidCompensation * 5.0f, -600, 600);
                if (extraMouseDx != 0)
                {
                    Simulation.SendInput?.Mouse?.MoveMouseBy(extraMouseDx, 0);
                }
                
                // 去除这里原先那种硬生生的堵塞 UI 线程的做法
                // 注释掉：await _waitUntilRotatedToAction(targetOrientation, 2);
            }
        }
        else
        {
            // 一旦追回，即刻抹除积分系历史，防止过调漂移回荡 (Overshoot)
            consecutiveCount = 0;
            _pidIntegral = 0;
        }

        _pidLastError = diff;
        return consecutiveCount;
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
                    if (_endJudgmentAction(screen))
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
                    var rawDistance = Navigation.GetDistance(waypoint, position);
                    var isPositionLost = (position.X == 0 && position.Y == 0) || rawDistance > 500;

                    if (isPositionLost)
                    {
                        lostRetryCount++;
                        if (lastValidPosition.HasValue && lostRetryCount <= maxLostRetryCount)
                        {
                            position = lastValidPosition.Value;
                            if (lostRetryCount == 1 || lostRetryCount % 3 == 0)
                                Logger.LogWarning("[精细寻路] 瞬时视觉失常，启用位置缓存作为保险坐标：({X:F1}, {Y:F1})", position.X, position.Y);
                            return BTStatus.Success; // 使用了兜底坐标，容许通过
                        }
                        
                        if (lostRetryCount % 3 == 0)
                            await _resolveAnomaliesAction(screen);

                        Logger.LogWarning("[精细寻路] 视觉坐标信号断开时间过长，安全停止本次路径微调...");
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
                            await _waitUntilRotatedToAction(targetOrientation, distance < 3.5f ? 5 : 2);
                            
                            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                            await Delay(distance < 3.5f ? pulseForwardMs / 2 : pulseForwardMs, _ct);
                            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                            
                            return BTStatus.Running; // 微位移循环结束，等待下一个帧
                        })
                    )
                )
            )
        );

        while (await microTicker.WaitForNextTickAsync(_ct))
        {
            stepsTaken++;
            using var currentScreen = _captureAction();
            screen = currentScreen;
            
            await rootNode.TickAsync();
            
            if (isRouteCompleted)
            {
                break; // 打断微循环计时流
            }
        }

        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        await Delay(1000, _ct);
        return exitBecauseEndJudgment;
    }
}
