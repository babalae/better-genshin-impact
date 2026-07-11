using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Model.Area;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using ActionEnum = BetterGenshinImpact.GameTask.AutoPathing.Model.Enum.ActionEnum;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPathing.Strategy;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// Path executor.
/// 负责自动寻路宏观逻辑调度，整合坐标推算、脱困、异常处理与战斗等子模块。
/// </summary>
public class PathExecutor
{
    internal readonly CameraRotateTask _rotateTask;
    internal readonly TrapEscaper _trapEscaper;
    internal readonly PathingAnomalyResolver _anomalyResolver;
    
    /// <summary>
    /// Gets the count of successful fights.
    /// 获取成功战斗的次数。
    /// </summary>
    public int SuccessFight { get; private set; } = 0;
    
    /// <summary>
    /// Increments the successful fight count.
    /// 增加成功战斗次数。
    /// </summary>
    public void IncrementSuccessFight() => SuccessFight++;
    
    /// <summary>
    /// Gets whether the path tracking successfully reached the end of all paths.
    /// 获取路径追踪是否完全走完所有路径结束的标识。
    /// </summary>
    public bool SuccessEnd { get; private set; } = false;
    
    internal PathingPartyManager _partyManager;
    internal readonly PathingNavigator _navigator;
    
    /// <summary>
    /// Gets the movement controller.
    /// 获取移动控制器。
    /// </summary>
    public PathingMovementController MovementController { get; }
    
    public Telemetry.RouteTelemetryManager RouteTelemetryManager { get; } = new();
    
    internal CancellationToken ct;
    internal PathExecutorSuspend pathExecutorSuspend;
    internal readonly PathingHealthController _healthController;
    private bool _autoPickPausedForInteractTeleport;

    /// <summary>
    /// Initializes the path executor.
    /// 构造路径执行器。
    /// </summary>
    /// <param name="ct">Cancellation token. 取消令牌。</param>
    public PathExecutor(CancellationToken ct)
    {
        _trapEscaper = new TrapEscaper(ct);
        _rotateTask = new CameraRotateTask(ct);
        this.ct = ct;
        pathExecutorSuspend = new PathExecutorSuspend(this);
        _healthController = PathingHealthControllerFactory.Create(ct, SwitchAvatar);
        _partyManager = new PathingPartyManager(ct, _healthController, null);
        _anomalyResolver = new PathingAnomalyResolver(ct, () => CaptureToRectArea(), () => PartyConfig.AutoSkipEnabled);
        _navigator = new PathingNavigator(ct, imageRegion => _anomalyResolver.ResolveAnomalies(imageRegion));
        MovementController = new PathingMovementController(
            ct,
            _navigator,
            _rotateTask,
            _trapEscaper,
            pathExecutorSuspend,
            new PathingMovementActions
            {
                CaptureAction = () => CaptureToRectArea(),
                EndJudgmentAction = EndJudgment,
                ResolveAnomaliesAction = ResolveAnomalies,
                WaitUntilRotatedToAction = WaitUntilRotatedTo,
                SwitchAvatarAction = index => SwitchAvatar(index),
                UseElementalSkillAction = UseElementalSkill,
                PartyConfigGetter = () => PartyConfig
            });
            
        MovementController.OnRouteTraversed = (prev, target, actualTraj, duration) => {
            RouteTelemetryManager.RecordSuccessfulRoute(prev, target, actualTraj, duration);
        };
        MovementController.OnRouteTraversalFailed = (prev, target, actualTraj, reason) => {
            RouteTelemetryManager.RecordFailedRoute(prev, target, actualTraj, reason);
        };
    }

    /// <summary>
    /// Gets or sets the party configuration.
    /// 获取或设置队伍配置。
    /// </summary>
    public PathingPartyConfig PartyConfig
    {
        get => _partyManager.PartyConfig;
        set => _partyManager.PartyConfig = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Judgment condition to terminate map tracking.
    /// 判断是否中止地图追踪的条件委托。
    /// </summary>
    public Func<ImageRegion, bool>? EndAction { get; set; }

    internal CombatScenes? _combatScenes => _partyManager.CombatScenes;

    internal const int RetryTimes = 2;

    private enum PathingSegmentResult
    {
        Completed,
        StopPathingSucceeded,
        StopPathingFailed
    }
    
    /// <summary>
    /// Gets or sets the current waypoint array.
    /// 获取或设置当前相关点位数组。
    /// </summary>
    public (int, List<WaypointForTrack>) CurWaypoints
    {
        get => _navigator.CurWaypoints;
        set => _navigator.CurWaypoints = value;
    }

    /// <summary>
    /// Gets or sets the current waypoint.
    /// 获取或设置当前点位。
    /// </summary>
    public (int, WaypointForTrack) CurWaypoint
    {
        get => _navigator.CurWaypoint;
        set => _navigator.CurWaypoint = value;
    }

    internal DateTime _lastGetExpeditionRewardsTime = DateTime.MinValue;

    /// <summary>
    /// Executes the pathing task.
    /// 执行寻路任务。
    /// </summary>
    /// <param name="task">Pathing task object. 寻路任务对象。</param>
    /// <returns>Asynchronous task. 异步任务。</returns>
    public async Task Pathing(PathingTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        SuccessEnd = false;
        SuccessFight = 0;

        const string sdKey = "PathExecutor";
        var sd = RunnerContext.Instance.SuspendableDictionary;
        sd.Remove(sdKey);

        RunnerContext.Instance.SuspendableDictionary.TryAdd(sdKey, pathExecutorSuspend);
        try
        {
            if (task.Positions == null || !task.Positions.Any())
            {
                Logger.LogWarning("没有路径点，寻路结束");
                return;
            }

            // 切换队伍
            if (!await _partyManager.SwitchPartyBefore(task))
            {
                return;
            }

            // 校验路径是否可以执行
            if (!await _partyManager.ValidateGameWithTask(task))
            {
                return;
            }

            InitializePathing(task);
            // 转换、按传送点分割路径
            var waypointsList = ConvertWaypointsForTrack(task.Positions, task);
            if (waypointsList.Count == 0)
            {
                Logger.LogWarning("没有可执行路径段，寻路结束");
                return;
            }

            await Delay(100, ct);
            Navigation.WarmUp(task.Info.MapMatchMethod); // 提前加载地图特征点

            for (var segmentIndex = 0; segmentIndex < waypointsList.Count; segmentIndex++)
            {
                var waypoints = waypointsList[segmentIndex];
                if (waypoints.Count == 0)
                {
                    continue;
                }

                Logger.LogInformation("开始执行路径段 {SegmentIndex}/{SegmentCount}，点位数 {WaypointCount}",
                    segmentIndex + 1,
                    waypointsList.Count,
                    waypoints.Count);

                var segmentResult = await ExecuteWaypointSegmentAsync(segmentIndex, waypoints, waypointsList);
                if (segmentResult == PathingSegmentResult.StopPathingSucceeded)
                {
                    SuccessEnd = true;
                    return;
                }

                if (segmentResult == PathingSegmentResult.StopPathingFailed)
                {
                    return;
                }
            }

            SuccessEnd = true;
        }
        finally
        {
            // 任务结束时清理暂停/恢复的临时状态，避免影响下一次路径执行。
            pathExecutorSuspend.Reset();
            IsPositionAndTimeSuspended = false;
            ResumeAutoPickForInteractTeleport();
            
            // 触发遥测落盘策略，强制当前积累的路线无论多少都全量写入 JSON 文件
            _ = RouteTelemetryManager.FlushAsync();
        }
    }

    public void PauseAutoPickForInteractTeleport()
    {
        if (_autoPickPausedForInteractTeleport)
        {
            return;
        }

        RunnerContext.Instance.StopAutoPick();
        _autoPickPausedForInteractTeleport = true;
        Logger.LogInformation("[寻路系统] 即将执行交互传送，已临时暂停自动拾取，避免提前触发交互。");
    }

    public void ResumeAutoPickForInteractTeleport()
    {
        if (!_autoPickPausedForInteractTeleport)
        {
            return;
        }

        RunnerContext.Instance.ResumeAutoPick();
        _autoPickPausedForInteractTeleport = false;
        Logger.LogInformation("[寻路系统] 交互传送处理结束，已恢复自动拾取。");
    }

    private async Task<PathingSegmentResult> ExecuteWaypointSegmentAsync(
        int segmentIndex,
        List<WaypointForTrack> waypoints,
        List<List<WaypointForTrack>> waypointsList)
    {
        RouteTelemetryManager.CurrentAnchorContext = waypoints.FirstOrDefault();
        CurWaypoints = (segmentIndex, waypoints);

        for (var i = 0; i < RetryTimes; i++)
        {
            try
            {
                await ResolveAnomalies(); // 异常场景处理

                // 如果首个点是非TP点位，强制设置在这个点位附近优先做局部匹配
                if (waypoints.Count > 0 && waypoints[0].Type != WaypointType.Teleport.Code)
                {
                    Navigation.SetPrevPosition((float)waypoints[0].X, (float)waypoints[0].Y);
                }

                for (var waypointIndex = 0; waypointIndex < waypoints.Count; waypointIndex++) // 一条路径段
                {
                    var waypoint = waypoints[waypointIndex];
                    CurWaypoint = (waypointIndex, waypoint);
                    PublishCurrentWaypoint(waypoint);
                    _navigator.TryCloseSkipOtherOperations();

                    var recoveryRes = await _healthController.CheckAndAttemptRecoveryAsync(waypoint, _combatScenes, PartyConfig, ct); // 低血量恢复
                    if (recoveryRes == Domain.HealthRecoveryResult.TeleportedToStatueRequiresRetry)
                    {
                        throw new RetryException("神像回血完成后重试路线");
                    }

                    var strategy = WaypointStrategyFactory.GetStrategy(waypoint.Type);
                    if (await strategy.ExecuteAsync(this, waypoint, waypointsList))
                    {
                        return PathingSegmentResult.StopPathingSucceeded;
                    }
                }

                return PathingSegmentResult.Completed;
            }
            catch (HandledException handledExc)
            {
                Logger.LogWarning(handledExc.Message);
                return PathingSegmentResult.StopPathingFailed;
            }
            catch (TaskCanceledException)
            {
                if (!RunnerContext.Instance.isAutoFetchDispatch && RunnerContext.Instance.IsContinuousRunGroup)
                {
                    throw;
                }

                return PathingSegmentResult.StopPathingFailed;
            }
            catch (RetryException retryException)
            {
                _navigator.StartSkipOtherOperations();
                Logger.LogWarning(retryException.Message);
                throw;
            }
            catch (RetryNoCountException retryException)
            {
                // 特殊情况下，重试不消耗次数
                i--;
                _navigator.StartSkipOtherOperations();
                Logger.LogWarning(retryException.Message);
            }
            finally
            {
                // 不管咋样，松开所有按键
                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
            }
        }

        return PathingSegmentResult.StopPathingFailed;
    }

    /// <summary>
    /// 判断是否是目标点位 / Checks if the waypoint is a target point.
    /// </summary>
    internal bool IsTargetPoint(WaypointForTrack waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);

        // 方位点不需要接近
        if (waypoint.Type == WaypointType.Orientation.Code || waypoint.Action == ActionEnum.UpDownGrabLeaf.Code)
        {
            return false;
        }

        var action = ActionEnum.GetEnumByCode(waypoint.Action);
        if (action is not null && action.UseWaypointTypeEnum != ActionUseWaypointTypeEnum.Custom)
        {
            // 强制点位类型的 action，以 action 为准
            return action.UseWaypointTypeEnum == ActionUseWaypointTypeEnum.Target;
        }

        // 其余情况和没有action的情况以点位类型为准
        return waypoint.Type == WaypointType.Target.Code;
    }

    /// <summary>
    /// 初始化寻路 / Initializes pathing.
    /// </summary>
    private void InitializePathing(PathingTask task)
    {
        LogScreenResolution();
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this,
            "UpdateCurrentPathing", new object(), task));
    }

    private void PublishCurrentWaypoint(WaypointForTrack waypoint)
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this,
            "UpdateCurrentWaypoint", new object(), waypoint));
    }

    /// <summary>
    /// 记录并校验屏幕分辨率 / Logs and validates screen resolution.
    /// </summary>
    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            Logger.LogError("游戏窗口分辨率不是 16:9 ！当前分辨率为 {Width}x{Height} , 非 16:9 分辨率的游戏无法正常使用地图追踪功能！",
                gameScreenSize.Width, gameScreenSize.Height);
            throw new NotSupportedException("游戏窗口分辨率不是 16:9 ！无法使用地图追踪功能！");
        }

        if (gameScreenSize.Width < 1920 || gameScreenSize.Height < 1080)
        {
            Logger.LogError("游戏窗口分辨率小于 1920x1080 ！当前分辨率为 {Width}x{Height} , 小于 1920x1080 的分辨率的游戏地图追踪的效果非常差！",
                gameScreenSize.Width, gameScreenSize.Height);
            throw new NotSupportedException("游戏窗口分辨率小于 1920x1080 ！无法使用地图追踪功能！");
        }
    }

    /// <summary>
    /// 转换路径点为可追踪点并根据传送点分区 / Converts path positions to trackable waypoints and splits by teleports.
    /// </summary>
    private List<List<WaypointForTrack>> ConvertWaypointsForTrack(List<Waypoint> positions, PathingTask task)
    {
        var result = new List<List<WaypointForTrack>>();
        var tempList = new List<WaypointForTrack>();

        if (positions == null) return result;

        var enableWaypointMisidentificationConfig = TaskContext.Instance().Config.PathingConditionConfig.EnableWaypointMisidentificationConfig;
        foreach (var p in positions)
        {
            if (p == null) continue;
            var pointExtParams = p.PointExtParams;

            var wft = new WaypointForTrack(p, task.Info.MapName, task.Info.MapMatchMethod)
            {
                Misidentification = enableWaypointMisidentificationConfig
                    ? pointExtParams?.Misidentification ?? new Waypoint.Misidentification()
                    : new Waypoint.Misidentification { Type = [] },
                MonsterTag = pointExtParams?.MonsterTag ?? string.Empty,
                EnableMonsterLootSplit = pointExtParams?.EnableMonsterLootSplit == true
            };

            if (wft.Type == WaypointType.Teleport.Code && tempList.Count > 0)
            {
                result.Add(tempList);
                tempList = new List<WaypointForTrack>();
            }

            tempList.Add(wft);
        }

        if (tempList.Count > 0)
        {
            result.Add(tempList);
        }

        return result;
    }

    /// <summary>
    /// 使用元素战技 / Uses elemental skill.
    /// </summary>
    private async Task UseElementalSkill()
    {
        if (string.IsNullOrEmpty(PartyConfig.GuardianAvatarIndex))
        {
            return;
        }

        await Delay(200, ct);

        // 切人
        Logger.LogInformation("切换盾、回血角色，使用元素战技");
        var avatar = await SwitchAvatar(PartyConfig.GuardianAvatarIndex, true);
        if (avatar == null)
        {
            return;
        }

        // TODO: 角色特性应下放至角色模型/配置文件中，而非在寻路器层硬编码。当前仅作提取隔离。
        bool isBackwardsSkill = avatar.Name == "钟离";
        
        if (isBackwardsSkill)
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            await Delay(50, ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveBackward);
            await Delay(200, ct);
        }

        avatar.UseSkill(PartyConfig.GuardianElementalSkillLongPress);

        if (isBackwardsSkill)
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        }
    }

    private async Task BeforeMoveCloseToTarget(WaypointForTrack waypoint)
    {
        if (waypoint.MoveMode == MoveModeEnum.Fly.Code && waypoint.Action == ActionEnum.StopFlying.Code)
        {
            var handler = ActionFactory.GetAfterHandler(ActionEnum.StopFlying.Code);
            if (handler != null)
            {
                await handler.RunAsync(ct, waypoint);
            }
        }
    }

    private async Task BeforeMoveToTarget(WaypointForTrack waypoint)
    {
        if (waypoint.Action == ActionEnum.UpDownGrabLeaf.Code)
        {
            Simulation.SendInput.Mouse.MiddleButtonClick();
            await Delay(300, ct);
            var screen = CaptureToRectArea();
            var position = await _navigator.GetPosition(screen, waypoint);
            var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            await WaitUntilRotatedTo(targetOrientation, 10);
            var handler = ActionFactory.GetBeforeHandler(waypoint.Action);
            if (handler != null)
            {
                await handler.RunAsync(ct, waypoint);
            }
        }
        else if (waypoint.Action == ActionEnum.LogOutput.Code)
        {
            Logger.LogInformation(waypoint.LogInfo);
        }
    }

    private async Task AfterMoveToTarget(WaypointForTrack waypoint)
    {
        if (waypoint.Action == ActionEnum.NahidaCollect.Code
            || waypoint.Action == ActionEnum.PickAround.Code
            || waypoint.Action == ActionEnum.Fight.Code
            || waypoint.Action == ActionEnum.HydroCollect.Code
            || waypoint.Action == ActionEnum.ElectroCollect.Code
            || waypoint.Action == ActionEnum.AnemoCollect.Code
            || waypoint.Action == ActionEnum.PyroCollect.Code
            || waypoint.Action == ActionEnum.CombatScript.Code
            || waypoint.Action == ActionEnum.Mining.Code
            || waypoint.Action == ActionEnum.LinneaMining.Code
            || waypoint.Action == ActionEnum.Fishing.Code
            || waypoint.Action == ActionEnum.ExitAndRelogin.Code
            || waypoint.Action == ActionEnum.EnterAndExitWonderland.Code
            || waypoint.Action == ActionEnum.SetTime.Code
            || waypoint.Action == ActionEnum.UseGadget.Code
            || waypoint.Action == ActionEnum.PickUpCollect.Code)
        {
            var handler = ActionFactory.GetAfterHandler(waypoint.Action);
            //,PartyConfig
            if (handler == null)
            {
                return;
            }

            await handler.RunAsync(ct, waypoint, PartyConfig);
            //统计结束战斗的次数
            if (waypoint.Action == ActionEnum.Fight.Code)
            {
                SuccessFight++;
            }
            await Delay(1000, ct);
        }
    }

    private async Task<Avatar?> SwitchAvatar(string index, bool needSkill = false)
    {
        return await _partyManager.SwitchAvatar(index, needSkill);
    }
    
    /// <summary>
    /// Interpolates position coordinates by time.
    /// 根据时间插值计算点位坐标。
    /// </summary>
    /// <param name="startPoint">Start point. 起始点位。</param>
    /// <summary>
    /// Gets or sets the position resolution suspend flag.
    /// 获取或设置位置解析挂起标识。
    /// </summary>
    public bool IsPositionAndTimeSuspended
    {
        get => _navigator.IsPositionAndTimeSuspended;
        set => _navigator.IsPositionAndTimeSuspended = value;
    }

    /// <summary>
    /// 等待直到旋转到目标视口 / Waits until rotated to the target orientation.
    /// </summary>
    internal async Task WaitUntilRotatedTo(int targetOrientation, int maxDiff)
    {
        if (await _rotateTask.WaitUntilRotatedTo(targetOrientation, maxDiff))
        {
            return;
        }
        await ResolveAnomalies();
        await _rotateTask.WaitUntilRotatedTo(targetOrientation, maxDiff);
    }

    /// <summary>
    /// 处理各类异常场景 / Resolves various anomaly scenarios.
    /// </summary>
    private async Task ResolveAnomalies(ImageRegion? imageRegion = null)
    {
        await _anomalyResolver.ResolveAnomalies(imageRegion);
    }

    /// <summary>
    /// 结束条件判断 / End judgment condition.
    /// </summary>
    private bool EndJudgment(ImageRegion ra)
    {
        if (EndAction != null && EndAction(ra))
        {
            Logger.LogInformation("达成结束条件，结束地图追踪");
            return true;
        }
        return false;
    }
}
