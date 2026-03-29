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

public class PathExecutor
{
    internal readonly CameraRotateTask _rotateTask;
    internal readonly TrapEscaper _trapEscaper;
    internal readonly PathingAnomalyResolver _anomalyResolver;
    public int SuccessFight { get; private set; } = 0;
    public void IncrementSuccessFight() => SuccessFight++;
    //路径追踪完全走完所有路径结束的标识
    public bool SuccessEnd { get; private set; } = false;
    internal PathingPartyManager _partyManager;
    internal readonly PathingNavigator _navigator;
    public PathingMovementController MovementController { get; }
    internal CancellationToken ct;
    internal PathExecutorSuspend pathExecutorSuspend;
    internal readonly PathingHealthController _healthController;

    public PathExecutor(CancellationToken ct)
    {
        _trapEscaper = new(ct);
        _rotateTask = new(ct);
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
            img => EndAction?.Invoke(img) ?? false,
            ResolveAnomalies,
            WaitUntilRotatedTo,
            index => SwitchAvatar(index),
            UseElementalSkill,
            () => PartyConfig);
    }

    public PathingPartyConfig PartyConfig
    {
        get => _partyManager.PartyConfig;
        set => _partyManager.PartyConfig = value;
    }

    /// <summary>
    /// 判断是否中止地图追踪的条件
    /// </summary>
    public Func<ImageRegion, bool>? EndAction { get; set; }

    internal CombatScenes? _combatScenes => _partyManager.CombatScenes;
    // private readonly Dictionary<string, string> _actionAvatarIndexMap = new();

    internal const int RetryTimes = 2;
    //记录当前相关点位数组
    public (int, List<WaypointForTrack>) CurWaypoints
    {
        get => _navigator.CurWaypoints;
        set => _navigator.CurWaypoints = value;
    }

    //记录当前点位
    public (int, WaypointForTrack) CurWaypoint
    {
        get => _navigator.CurWaypoint;
        set => _navigator.CurWaypoint = value;
    }

    // 最近一次获取派遣奖励的时间
    internal DateTime _lastGetExpeditionRewardsTime = DateTime.MinValue;


    //当到达恢复点位
    

    

    public async Task Pathing(PathingTask task)
    {
        // SuspendableDictionary;
        const string sdKey = "PathExecutor";
        var sd = RunnerContext.Instance.SuspendableDictionary;
        sd.Remove(sdKey);

        RunnerContext.Instance.SuspendableDictionary.TryAdd(sdKey, pathExecutorSuspend);
        try
        {
            if (!task.Positions.Any())
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
                                throw new BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception.RetryException("神像回血完成后重试路线");
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
                        break;
                    }
                    
                    catch (TaskCanceledException)
                    {
                        if (!RunnerContext.Instance.isAutoFetchDispatch && RunnerContext.Instance.IsContinuousRunGroup)
                        {
                            throw;
                        }
                        else
                        {
                            break;
                        }
                    }
                    catch (RetryException retryException)
                    {
                        _navigator.StartSkipOtherOperations();
                        Logger.LogWarning(retryException.Message);
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
                        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
                        Simulation.SendInput.Mouse.RightButtonUp();
                    }
                }

            }
        }
        finally
        {
            // 任务结束时清理暂停/恢复的临时状态，避免影响下一次路径执行。
            pathExecutorSuspend.Reset();
            GetPositionAndTimeSuspendFlag = false;
        }
    }

    internal bool IsTargetPoint(WaypointForTrack waypoint)
    {
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

    private void InitializePathing(PathingTask task)
    {
        LogScreenResolution();
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this,
            "UpdateCurrentPathing", new object(), task));
    }

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

    private List<List<WaypointForTrack>> ConvertWaypointsForTrack(List<Waypoint> positions, PathingTask task)
    {
        var result = new List<List<WaypointForTrack>>();
        var tempList = new List<WaypointForTrack>();

        foreach (var p in positions)
        {
            var wft = new WaypointForTrack(p, task.Info.MapName, task.Info.MapMatchMethod)
            {
                Misidentification = p.PointExtParams.Misidentification,
                MonsterTag = p.PointExtParams.MonsterTag,
                EnableMonsterLootSplit = p.PointExtParams.EnableMonsterLootSplit
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

    

    private async Task<Avatar?> SwitchAvatar(string index, bool needSkill = false)
    {
        return await _partyManager.SwitchAvatar(index, needSkill);
    }
    
    public static Point2f InterpolatePointByTime(
        Point2f startPoint,
        Point2f endPoint,
        DateTime startTime,
        DateTime midTime,
        DateTime endTime)
    {
        return PathingNavigator.InterpolatePointByTime(startPoint, endPoint, startTime, midTime, endTime);
    }

    public bool GetPositionAndTimeSuspendFlag
    {
        get => _navigator.GetPositionAndTimeSuspendFlag;
        set => _navigator.GetPositionAndTimeSuspendFlag = value;
    }

    internal async Task WaitUntilRotatedTo(int targetOrientation, int maxDiff)
    {
        if (await _rotateTask.WaitUntilRotatedTo(targetOrientation, maxDiff))
        {
            return;
        }
        await ResolveAnomalies();
        await _rotateTask.WaitUntilRotatedTo(targetOrientation, maxDiff);
    }

    /**
     * 处理各种异常场景
     * 需要保证耗时不能太高
     */
    private async Task ResolveAnomalies(ImageRegion? imageRegion = null)
    {
        await _anomalyResolver.ResolveAnomalies(imageRegion);
    }

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