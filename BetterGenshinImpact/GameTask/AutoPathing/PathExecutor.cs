using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using BetterGenshinImpact.GameTask.Common;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static BetterGenshinImpact.GameTask.SystemControl;
using ActionEnum = BetterGenshinImpact.GameTask.AutoPathing.Model.Enum.ActionEnum;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.AutoFight;
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
    
    internal CancellationToken ct;
    internal PathExecutorSuspend pathExecutorSuspend;
    internal readonly PathingHealthController _healthController;

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
            () => CaptureToRectArea(),
            EndJudgment,
            ResolveAnomalies,
            WaitUntilRotatedTo,
            index => SwitchAvatar(index),
            UseElementalSkill,
            () => PartyConfig);
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

            await Delay(100, ct);
            Navigation.WarmUp(task.Info.MapMatchMethod); // 提前加载地图特征点

            foreach (var waypoints in waypointsList) // 按传送点分割的路径
            {
                CurWaypoints = (waypointsList.FindIndex(wps => wps == waypoints), waypoints);
                for (var i = 0; i < RetryTimes; i++)
                {
                    try
                    {
                        await ResolveAnomalies(); // 异常场景处理

                        // 如果首个点是非TP点位，强制设置在这个点位附近优先做局部匹配
                        if (waypoints[0].Type != WaypointType.Teleport.Code)
                        {
                            Navigation.SetPrevPosition((float)waypoints[0].X, (float)waypoints[0].Y);
                        }

                        foreach (var waypoint in waypoints) // 一条路径
                        {
                            CurWaypoint = (waypoints.FindIndex(wps => wps == waypoint), waypoint);
                            _navigator.TryCloseSkipOtherOperations();
                            
                            var recoveryRes = await _healthController.CheckAndAttemptRecoveryAsync(waypoint, _combatScenes, PartyConfig, ct); // 低血量恢复
                            if (recoveryRes == Domain.HealthRecoveryResult.TeleportedToStatueRequiresRetry)
                            {
                                throw new RetryException("神像回血完成后重试路线");
                            }

                            var strategy = WaypointStrategyFactory.GetStrategy(waypoint.Type);
                            if (await strategy.ExecuteAsync(this, waypoint, waypointsList))
                            {
                                SuccessEnd = true;
                                return;
                            }
                        }

                        if (waypoints == waypointsList.Last())
                        {
                            SuccessEnd = true;
                        }
                        break;
                    }
                    catch (HandledException handledExc)
                    {
                        Logger.LogWarning(handledExc.Message);
                        return;
                    }
                    catch (TaskCanceledException)
                    {
                        if (!RunnerContext.Instance.isAutoFetchDispatch && RunnerContext.Instance.IsContinuousRunGroup)
                        {
                            throw;
                        }
                        else
                        {
                            return;
                        }
                    }
                    catch (RetryException retryException)
                    {
                        _navigator.StartSkipOtherOperations();
                        Logger.LogWarning(retryException.Message);
                        if (i == RetryTimes - 1)
                        {
                            return;
                        }
                    }
                    catch (RetryNoCountException retryException)
                    {
                        //特殊情况下，重试不消耗次数
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
            }
        }
        finally
        {
            // 任务结束时清理暂停/恢复的临时状态，避免影响下一次路径执行。
            pathExecutorSuspend.Reset();
            IsPositionAndTimeSuspended = false;
        }
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

        foreach (var p in positions)
        {
            if (p == null) continue;

            var wft = new WaypointForTrack(p, task.Info.MapName, task.Info.MapMatchMethod)
            {
                Misidentification = p.PointExtParams.Misidentification,
                MonsterTag = p.PointExtParams.MonsterTag,
                EnableMonsterLootSplit = p.PointExtParams != null && p.PointExtParams.EnableMonsterLootSplit
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

    /// <summary>
    /// 切换角色 / Switches avatar.
    /// </summary>
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