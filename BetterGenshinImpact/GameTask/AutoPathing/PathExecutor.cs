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

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathExecutor
{
    private readonly CameraRotateTask _rotateTask;
    private readonly TrapEscaper _trapEscaper;
    private readonly BlessingOfTheWelkinMoonTask _blessingOfTheWelkinMoonTask = new();
    private AutoSkipTrigger? _autoSkipTrigger;

    private PathingPartyConfig? _partyConfig;
    private CancellationToken ct;
    private PathExecutorSuspend pathExecutorSuspend;

    public PathExecutor(CancellationToken ct)
    {
        _trapEscaper = new(ct);
        _rotateTask = new(ct);
        this.ct = ct;
        pathExecutorSuspend = new PathExecutorSuspend(this);
    }

    public PathingPartyConfig PartyConfig
    {
        get => _partyConfig ?? PathingPartyConfig.BuildDefault();
        set => _partyConfig = value;
    }

    /// <summary>
    /// 判断是否中止地图追踪的条件
    /// </summary>
    public Func<ImageRegion, bool>? EndAction { get; set; }

    private CombatScenes? _combatScenes;
    // private readonly Dictionary<string, string> _actionAvatarIndexMap = new();

    private DateTime _elementalSkillLastUseTime = DateTime.MinValue;
    private DateTime _useGadgetLastUseTime = DateTime.MinValue;

    private const int RetryTimes = 2;
    private int _inTrap = 0;


    //记录当前相关点位数组
    public (int, List<WaypointForTrack>) CurWaypoints { get; set; }

    //记录当前点位
    public (int, WaypointForTrack) CurWaypoint { get; set; }

    //记录恢复点位数组
    private (int, List<WaypointForTrack>) RecordWaypoints { get; set; }

    //记录恢复点位
    private (int, WaypointForTrack) RecordWaypoint { get; set; }

    //跳过除走路径以外的操作
    private bool _skipOtherOperations = false;

    // 最近一次获取派遣奖励的时间
    private DateTime _lastGetExpeditionRewardsTime = DateTime.MinValue;


    //当到达恢复点位
    public void TryCloseSkipOtherOperations()
    {
        // Logger.LogWarning("判断是否跳过地图追踪:" + (CurWaypoint.Item1 < RecordWaypoint.Item1));
        if (RecordWaypoints == CurWaypoints && CurWaypoint.Item1 < RecordWaypoint.Item1)
        {
            return;
        }

        if (_skipOtherOperations)
        {
            Logger.LogWarning("已到达上次点位，地图追踪功能恢复");
        }

        _skipOtherOperations = false;
    }

    //记录点位，方便后面恢复
    public void StartSkipOtherOperations()
    {
        Logger.LogWarning("记录恢复点位，地图追踪将到达上次点位之前将跳过走路之外的操作");
        _skipOtherOperations = true;
        RecordWaypoints = CurWaypoints;
        RecordWaypoint = CurWaypoint;
    }

    public async Task Pathing(PathingTask task)
    {
        // SuspendableDictionary;
        const string sdKey = "PathExecutor";
        var sd = RunnerContext.Instance.SuspendableDictionary;
        sd.Remove(sdKey);

        RunnerContext.Instance.SuspendableDictionary.TryAdd(sdKey, pathExecutorSuspend);

        if (!task.Positions.Any())
        {
            Logger.LogWarning("没有路径点，寻路结束");
            return;
        }


        // 切换队伍
        if (!await SwitchPartyBefore(task))
        {
            return;
        }

        // 校验路径是否可以执行
        if (!await ValidateGameWithTask(task))
        {
            return;
        }

        InitializePathing(task);
        // 转换、按传送点分割路径
        var waypointsList = ConvertWaypointsForTrack(task.Positions, task);

        await Delay(100, ct);
        Navigation.WarmUp(); // 提前加载地图特征点

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
                        TryCloseSkipOtherOperations();
                        await RecoverWhenLowHp(waypoint); // 低血量恢复

                        if (waypoint.Type == WaypointType.Teleport.Code)
                        {
                            await HandleTeleportWaypoint(waypoint);
                        }
                        else
                        {
                            await BeforeMoveToTarget(waypoint);
                            // Path不用走得很近，Target需要接近，但都需要先移动到对应位置
                            if (waypoint.Type == WaypointType.Orientation.Code)
                            {
                                // 方位点，只需要朝向
                                // 考虑到方位点大概率是作为执行action的最后一个点，所以放在此处处理，不和传送点一样单独处理
                                await FaceTo(waypoint);
                            }
                            else if (waypoint.Action != ActionEnum.UpDownGrabLeaf.Code)
                            {
                                await MoveTo(waypoint);
                            }

                            await BeforeMoveCloseToTarget(waypoint);

                            if (IsTargetPoint(waypoint))
                            {
                                await MoveCloseTo(waypoint);
                            }

                            //skipOtherOperations如果重试，则跳过相关操作，
                            if ((!string.IsNullOrEmpty(waypoint.Action) && !_skipOtherOperations) ||
                                waypoint.Action == ActionEnum.CombatScript.Code)
                            {
                                // 执行 action
                                await AfterMoveToTarget(waypoint);
                            }
                        }
                    }

                    break;
                }
                catch (NormalEndException normalEndException)
                {
                    Logger.LogInformation(normalEndException.Message);
                    if (!RunnerContext.Instance.isAutoFetchDispatch && RunnerContext.Instance.IsContinuousRunGroup)
                    {
                        throw;
                    }
                    else
                    {
                        break;
                    }
                }
                catch (TaskCanceledException e)
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
                    StartSkipOtherOperations();
                    Logger.LogWarning(retryException.Message);
                }
                catch (RetryNoCountException retryException)
                {
                    //特殊情况下，重试不消耗次数
                    i--;
                    StartSkipOtherOperations();
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

    private bool IsTargetPoint(WaypointForTrack waypoint)
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

    private async Task<bool> SwitchPartyBefore(PathingTask task)
    {
        var ra = CaptureToRectArea();

        // 切换队伍前判断是否全队死亡 // 可能队伍切换失败导致的死亡
        if (Bv.ClickIfInReviveModal(ra))
        {
            await Bv.WaitForMainUi(ct); // 等待主界面加载完成
            Logger.LogInformation("复苏完成");
            await Delay(4000, ct);
            // 血量肯定不满，直接去七天神像回血
            await TpStatueOfTheSeven();
        }

        var pRaList = ra.FindMulti(AutoFightAssets.Instance.PRa); // 判断是否联机
        if (pRaList.Count > 0)
        {
            Logger.LogInformation("处于联机状态下，不切换队伍");
        }
        else
        {
            if (PartyConfig is { Enabled: false })
            {
                // 调度器未配置的情况下，根据地图追踪条件配置切换队伍
                var partyName = FilterPartyNameByConditionConfig(task);
                if (!await SwitchParty(partyName))
                {
                    Logger.LogError("切换队伍失败，无法执行此路径！请检查地图追踪设置！");
                    return false;
                }
            }
            else if (!string.IsNullOrEmpty(PartyConfig.PartyName))
            {
                if (!await SwitchParty(PartyConfig.PartyName))
                {
                    Logger.LogError("切换队伍失败，无法执行此路径！请检查配置组中的地图追踪配置！");
                    return false;
                }
            }
        }

        return true;
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
            throw new Exception("游戏窗口分辨率不是 16:9 ！无法使用地图追踪功能！");
        }

        if (gameScreenSize.Width < 1920 || gameScreenSize.Height < 1080)
        {
            Logger.LogError("游戏窗口分辨率小于 1920x1080 ！当前分辨率为 {Width}x{Height} , 小于 1920x1080 的分辨率的游戏地图追踪的效果非常差！",
                gameScreenSize.Width, gameScreenSize.Height);
            throw new Exception("游戏窗口分辨率小于 1920x1080 ！无法使用地图追踪功能！");
        }
    }

    /// <summary>
    /// 切换队伍
    /// </summary>
    /// <param name="partyName"></param>
    /// <returns></returns>
    private async Task<bool> SwitchParty(string? partyName)
    {
        bool success = true;
        if (!string.IsNullOrEmpty(partyName))
        {
            if (RunnerContext.Instance.PartyName == partyName)
            {
                return success;
            }

            bool forceTp = PartyConfig.IsVisitStatueBeforeSwitchParty;

            if (forceTp) // 强制传送模式
            {
                await new TpTask(ct).TpToStatueOfTheSeven(); // fix typos
                success = await new SwitchPartyTask().Start(partyName, ct);
            }
            else // 优先原地切换模式
            {
                try
                {
                    success = await new SwitchPartyTask().Start(partyName, ct);
                }
                catch (PartySetupFailedException)
                {
                    await new TpTask(ct).TpToStatueOfTheSeven();
                    success = await new SwitchPartyTask().Start(partyName, ct);
                }
            }

            if (success)
            {
                RunnerContext.Instance.PartyName = partyName;
                RunnerContext.Instance.ClearCombatScenes();
            }
        }

        return success;
    }


    private static string? FilterPartyNameByConditionConfig(PathingTask task)
    {
        var pathingConditionConfig = TaskContext.Instance().Config.PathingConditionConfig;
        var materialName = task.GetMaterialName();
        var specialActions = task.Positions
            .Select(p => p.Action)
            .Where(action => !string.IsNullOrEmpty(action))
            .Distinct()
            .ToList();
        var partyName = pathingConditionConfig.FilterPartyName(materialName, specialActions);
        return partyName;
    }

    /// <summary>
    /// 校验
    /// </summary>
    /// <param name="task"></param>
    /// <returns></returns>
    private async Task<bool> ValidateGameWithTask(PathingTask task)
    {
        _combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
        if (_combatScenes == null)
        {
            return false;
        }

        // 没有强制配置的情况下，使用地图追踪内的条件配置
        // 必须放在这里，因为要通过队伍识别来得到最终结果
        var pathingConditionConfig = TaskContext.Instance().Config.PathingConditionConfig;
        if (PartyConfig is { Enabled: false })
        {
            PartyConfig = pathingConditionConfig.BuildPartyConfigByCondition(_combatScenes);
        }

        // 校验角色是否存在
        if (task.HasAction(ActionEnum.NahidaCollect.Code))
        {
            var avatar = _combatScenes.SelectAvatar("纳西妲");
            if (avatar == null)
            {
                Logger.LogError("此路径存在纳西妲收集动作，队伍中没有纳西妲角色，无法执行此路径！");
                return false;
            }

            // _actionAvatarIndexMap.Add("nahida_collect", avatar.Index.ToString());
        }

        // 把所有需要切换的角色编号记录下来
        Dictionary<string, ElementalType> map = new()
        {
            { ActionEnum.HydroCollect.Code, ElementalType.Hydro },
            { ActionEnum.ElectroCollect.Code, ElementalType.Electro },
            { ActionEnum.AnemoCollect.Code, ElementalType.Anemo }
        };

        foreach (var (action, el) in map)
        {
            if (!ValidateElementalActionAvatarIndex(task, action, el, _combatScenes))
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidateElementalActionAvatarIndex(PathingTask task, string action, ElementalType el,
        CombatScenes combatScenes)
    {
        if (task.HasAction(action))
        {
            foreach (var avatar in combatScenes.GetAvatars())
            {
                if (ElementalCollectAvatarConfigs.Get(avatar.Name, el) != null)
                {
                    return true;
                }
            }

            Logger.LogError("此路径存在 {El}元素采集 动作，队伍中没有对应元素角色:{Names}，无法执行此路径！", el.ToChinese(),
                string.Join(",", ElementalCollectAvatarConfigs.GetAvatarNameList(el)));
            return false;
        }
        else
        {
            return true;
        }
    }

    private List<List<WaypointForTrack>> ConvertWaypointsForTrack(List<Waypoint> positions, PathingTask task)
    {
        // 把 X Y 转换为 MatX MatY
        var allList = positions.Select(waypoint =>
        {
            WaypointForTrack wft=new WaypointForTrack(waypoint, task.Info.MapName);
            wft.Misidentification=waypoint.PointExtParams.Misidentification;
            wft.MonsterTag = waypoint.PointExtParams.MonsterTag;
            wft.EnableMonsterLootSplit = waypoint.PointExtParams.EnableMonsterLootSplit;
            return wft;
        }).ToList();

        // 按照WaypointType.Teleport.Code切割数组
        var result = new List<List<WaypointForTrack>>();
        var tempList = new List<WaypointForTrack>();
        foreach (var waypoint in allList)
        {
            if (waypoint.Type == WaypointType.Teleport.Code)
            {
                if (tempList.Count > 0)
                {
                    result.Add(tempList);
                    tempList = new List<WaypointForTrack>();
                }
            }

            tempList.Add(waypoint);
        }

        result.Add(tempList);

        return result;
    }

    /// <summary>
    /// 尝试队伍回血，如果单人回血，由于记录检查时是哪位残血，则当作行走位处理。
    /// </summary>
    private async Task<bool> TryPartyHealing()
    {
        if (_combatScenes is null) return false;
        foreach (var avatar in _combatScenes.GetAvatars())
        {
            if (avatar.Name == "白术")
            {
                if (avatar.TrySwitch())
                {
                    //1命白术能两次
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                    await Delay(800, ct);
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                    await Delay(800, ct);
                    await SwitchAvatar(PartyConfig.MainAvatarIndex);
                    await Delay(4000, ct);
                    return true;
                }

                break;
            }
            else if (avatar.Name == "希格雯")
            {
                if (avatar.TrySwitch())
                {
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                    await Delay(11000, ct);
                    await SwitchAvatar(PartyConfig.MainAvatarIndex);
                    return true;
                }

                break;
            }
            else if (avatar.Name == "珊瑚宫心海")
            {
                if (avatar.TrySwitch())
                {
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                    await Delay(500, ct);
                    //尝试Q全队回血
                    Simulation.SendInput.SimulateAction(GIActions.ElementalBurst);
                    //单人血只给行走位加血
                    await SwitchAvatar(PartyConfig.MainAvatarIndex);
                    await Delay(5000, ct);
                    return true;
                }
            }
        }


        return false;
    }

    private async Task RecoverWhenLowHp(WaypointForTrack waypoint)
    {
        if (PartyConfig.OnlyInTeleportRecover && waypoint.Type != WaypointType.Teleport.Code)
        {
            return;
        }

        using var region = CaptureToRectArea();
        if (Bv.CurrentAvatarIsLowHp(region) && !(await TryPartyHealing() && Bv.CurrentAvatarIsLowHp(region)))
        {
            Logger.LogInformation("当前角色血量过低，去七天神像恢复");
            await TpStatueOfTheSeven();
            throw new RetryException("回血完成后重试路线");
        }
        else if (Bv.ClickIfInReviveModal(region))
        {
            await Bv.WaitForMainUi(ct); // 等待主界面加载完成
            Logger.LogInformation("复苏完成");
            await Delay(4000, ct);
            // 血量肯定不满，直接去七天神像回血
            await TpStatueOfTheSeven();
            throw new RetryException("回血完成后重试路线");
        }
    }

    private async Task TpStatueOfTheSeven()
    {
        // tp 到七天神像回血
        var tpTask = new TpTask(ct);
        await RunnerContext.Instance.StopAutoPickRunTask(async () => await tpTask.TpToStatueOfTheSeven(), 5);
        Logger.LogInformation("血量恢复完成。【设置】-【七天神像设置】可以修改回血相关配置。");
    }

    /// <summary>
    /// 尝试自动领取派遣奖励，
    /// </summary>
    /// <returns>是否可以领取派遣奖励</returns>
    private async Task<bool> TryGetExpeditionRewardsDispatch(TpTask? tpTask = null)
    {
        if (tpTask == null)
        {
            tpTask = new TpTask(ct);
        }

        // 最小5分钟间隔
        if ((DateTime.UtcNow - _lastGetExpeditionRewardsTime).TotalMinutes < 5)
        {
            return false;
        }

        //打开大地图操作
        await tpTask.OpenBigMapUi();
        bool changeBigMap = false;
        string adventurersGuildCountry =
            TaskContext.Instance().Config.OtherConfig.AutoFetchDispatchAdventurersGuildCountry;
        if (!RunnerContext.Instance.isAutoFetchDispatch && adventurersGuildCountry != "无")
        {
            var ra1 = CaptureToRectArea();

            var textRect = new Rect(60, 20, 160, 260);
            var textMat = new Mat(ra1.SrcMat, textRect);
            string text = OcrFactory.Paddle.Ocr(textMat);
            if (text.Contains("探索派遣奖励"))
            {
                changeBigMap = true;
                Logger.LogInformation("开始自动领取派遣任务！");
                try
                {
                    RunnerContext.Instance.isAutoFetchDispatch = true;
                    await RunnerContext.Instance.StopAutoPickRunTask(
                        async () => await new GoToAdventurersGuildTask().Start(adventurersGuildCountry, ct, null, true),
                        5);
                    Logger.LogInformation("自动领取派遣结束，回归原任务！");
                }
                catch (Exception e)
                {
                    Logger.LogInformation("未知原因，发生异常，尝试继续执行任务！");
                }
                finally
                {
                    RunnerContext.Instance.isAutoFetchDispatch = false;
                    _lastGetExpeditionRewardsTime = DateTime.UtcNow; // 无论成功与否都更新时间
                }
            }
        }

        return changeBigMap;
    }

    private async Task HandleTeleportWaypoint(WaypointForTrack waypoint)
    {
        var forceTp = waypoint.Action == ActionEnum.ForceTp.Code;
        TpTask tpTask = new TpTask(ct);
        await TryGetExpeditionRewardsDispatch(tpTask);
        var (tpX, tpY) = await tpTask.Tp(waypoint.GameX, waypoint.GameY, waypoint.MapName, forceTp);
        var (tprX, tprY) = MapManager.GetMap(waypoint.MapName)
            .ConvertGenshinMapCoordinatesToImageCoordinates((float)tpX, (float)tpY);
        Navigation.SetPrevPosition(tprX, tprY); // 通过上一个位置直接进行局部特征匹配
        await Delay(500, ct); // 多等一会
    }

    public async Task FaceTo(WaypointForTrack waypoint)
    {
        var screen = CaptureToRectArea();
        var position = await GetPosition(screen, waypoint);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogDebug("朝向点，位置({x2},{y2})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        await _rotateTask.WaitUntilRotatedTo(targetOrientation, 2);
        await Delay(500, ct);
    }

    public DateTime moveToStartTime;

    public async Task MoveTo(WaypointForTrack waypoint)
    {
        // 切人
        await SwitchAvatar(PartyConfig.MainAvatarIndex);

        var screen = CaptureToRectArea();
        var (position, additionalTimeInMs) = await GetPositionAndTime(screen, waypoint);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogDebug("粗略接近途经点，位置({x2},{y2})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);
        moveToStartTime = DateTime.UtcNow;
        var lastPositionRecord = DateTime.UtcNow;
        var fastMode = false;
        var prevPositions = new List<Point2f>();
        var fastModeColdTime = DateTime.MinValue;
        var num = 0;

        // 按下w，一直走
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        while (!ct.IsCancellationRequested)
        {
            if (!Simulation.IsKeyDown(GIActions.MoveForward.ToActionKey().ToVK()))
            {
                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            }

            num++;
            if ((DateTime.UtcNow - moveToStartTime).TotalSeconds > 240)
            {
                Logger.LogWarning("执行超时，放弃此次追踪");
                throw new RetryException("路径点执行超时，放弃整条路径");
            }

            screen = CaptureToRectArea();

            EndJudgment(screen);

            // position = await GetPosition(screen, waypoint);
             (position, additionalTimeInMs) = await GetPositionAndTime(screen, waypoint);
             if (additionalTimeInMs>0)
             {
                 if (!Simulation.IsKeyDown(GIActions.MoveForward.ToActionKey().ToVK()))
                 {
                     Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                 }

                 additionalTimeInMs = additionalTimeInMs + 1000;//当做起步补偿
             }
            var distance = Navigation.GetDistance(waypoint, position);
            Debug.WriteLine($"接近目标点中，距离为{distance}");
            if (distance < 4)
            {
                Logger.LogDebug("到达路径点附近");
                break;
            }

            if (distance > 500)
            {
                if (pathExecutorSuspend.CheckAndResetSuspendPoint())
                {
                    throw new RetryNoCountException("可能暂停导致路径过远，重试一次此路线！");
                }
                else
                {
                    Logger.LogWarning($"距离过远（{position.X},{position.Y}）->（{waypoint.X},{waypoint.Y}）={distance}，跳过路径点");
                }


                break;
            }

            // 非攀爬状态下，检测是否卡死（脱困触发器）
            if (waypoint.MoveMode != MoveModeEnum.Climb.Code)
            {
                if ((DateTime.UtcNow - lastPositionRecord).TotalMilliseconds > 1000 + additionalTimeInMs)
                {
                    lastPositionRecord = DateTime.UtcNow;
                    prevPositions.Add(position);
                    if (prevPositions.Count > 8)
                    {
                        var delta = prevPositions[^1] - prevPositions[^8];
                        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 3)
                        {
                            _inTrap++;
                            if (_inTrap > 2)
                            {
                                throw new RetryException("此路线出现3次卡死，重试一次路线或放弃此路线！");
                            }

                            Logger.LogWarning("疑似卡死，尝试脱离...");

                            //调用脱困代码，由TrapEscaper接管移动
                            await _trapEscaper.RotateAndMove();
                            await _trapEscaper.MoveTo(waypoint);
                            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                            Logger.LogInformation("卡死脱离结束");
                            continue;
                        }
                    }
                }
            }

            // 旋转视角
            targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            //执行旋转
            _rotateTask.RotateToApproach(targetOrientation, screen);

            // 根据指定方式进行移动
            if (waypoint.MoveMode == MoveModeEnum.Fly.Code)
            {
                var isFlying = Bv.GetMotionStatus(screen) == MotionStatus.Fly;
                if (!isFlying)
                {
                    Debug.WriteLine("未进入飞行状态，按下空格");
                    Simulation.SendInput.SimulateAction(GIActions.Jump);
                    await Delay(200, ct);
                }

                await Delay(200, ct);
                continue;
            }

            if (waypoint.MoveMode == MoveModeEnum.Jump.Code)
            {
                Simulation.SendInput.SimulateAction(GIActions.Jump);
                await Delay(200, ct);
                continue;
            }

            // 只有设置为run才会一直疾跑
            if (waypoint.MoveMode == MoveModeEnum.Run.Code)
            {
                if (distance > 20 != fastMode) // 距离大于20时可以使用疾跑/自由泳
                {
                    if (fastMode)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
                    }
                    else
                    {
                        Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyDown);
                    }

                    fastMode = !fastMode;
                }
            }
            else if (waypoint.MoveMode == MoveModeEnum.Dash.Code)
            {
                if (distance > 20) // 距离大于25时可以使用疾跑
                {
                    if (Math.Abs((fastModeColdTime - DateTime.UtcNow).TotalMilliseconds) > 1000) //冷却一会
                    {
                        fastModeColdTime = DateTime.UtcNow;
                        Simulation.SendInput.SimulateAction(GIActions.SprintMouse);
                    }
                }
            }
            else if (waypoint.MoveMode != MoveModeEnum.Climb.Code) //否则自动短疾跑
            {
                // 使用 E 技能
                if (distance > 10 && !string.IsNullOrEmpty(PartyConfig.GuardianAvatarIndex) &&
                    double.TryParse(PartyConfig.GuardianElementalSkillSecondInterval, out var s))
                {
                    if (s < 1)
                    {
                        Logger.LogWarning("元素战技冷却时间设置太短，不执行！");
                        return;
                    }

                    var ms = s * 1000;
                    if ((DateTime.UtcNow - _elementalSkillLastUseTime).TotalMilliseconds > ms)
                    {
                        // 可能刚切过人在冷却时间内
                        if (num <= 5 && (!string.IsNullOrEmpty(PartyConfig.MainAvatarIndex) &&
                                         PartyConfig.GuardianAvatarIndex != PartyConfig.MainAvatarIndex))
                        {
                            await Delay(800, ct); // 总共1s
                        }

                        await UseElementalSkill();
                        _elementalSkillLastUseTime = DateTime.UtcNow;
                    }
                }

                // 自动疾跑
                if (distance > 20 && PartyConfig.AutoRunEnabled)
                {
                    if (Math.Abs((fastModeColdTime - DateTime.UtcNow).TotalMilliseconds) > 2500) //冷却时间2.5s，回复体力用
                    {
                        fastModeColdTime = DateTime.UtcNow;
                        Simulation.SendInput.SimulateAction(GIActions.SprintMouse);
                    }
                }
            }

            // 使用小道具
            if (PartyConfig.UseGadgetIntervalMs > 0)
            {
                if ((DateTime.UtcNow - _useGadgetLastUseTime).TotalMilliseconds > PartyConfig.UseGadgetIntervalMs)
                {
                    Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
                    _useGadgetLastUseTime = DateTime.UtcNow;
                }
            }

            await Delay(100, ct);
        }

        // 抬起w键
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
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

        // 钟离往身后放柱子
        if (avatar.Name == "钟离")
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            await Delay(50, ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveBackward);
            await Delay(200, ct);
        }

        avatar.UseSkill(PartyConfig.GuardianElementalSkillLongPress);

        // 钟离往身后放柱子 后继续走路
        if (avatar.Name == "钟离")
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        }
    }

    private async Task MoveCloseTo(WaypointForTrack waypoint)
    {
        ImageRegion screen;
        Point2f position;
        int targetOrientation;
        Logger.LogDebug("精确接近目标点，位置({x2},{y2})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");

        var stepsTaken = 0;
        while (!ct.IsCancellationRequested)
        {
            stepsTaken++;
            if (stepsTaken > 25)
            {
                Logger.LogWarning("精确接近超时");
                break;
            }

            screen = CaptureToRectArea();

            EndJudgment(screen);

            position = await GetPosition(screen, waypoint);
            if (Navigation.GetDistance(waypoint, position) < 2)
            {
                Logger.LogDebug("已到达路径点");
                break;
            }

            targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            await _rotateTask.WaitUntilRotatedTo(targetOrientation, 2);
            // 小碎步接近
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            Thread.Sleep(60);
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            // Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W).Sleep(60).KeyUp(User32.VK.VK_W);
            await Delay(20, ct);
        }

        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);

        // 到达目的地后停顿一秒
        await Delay(1000, ct);
    }

    private async Task BeforeMoveCloseToTarget(WaypointForTrack waypoint)
    {
        if (waypoint.MoveMode == MoveModeEnum.Fly.Code && waypoint.Action == ActionEnum.StopFlying.Code)
        {
            await ActionFactory.GetBeforeHandler(ActionEnum.StopFlying.Code).RunAsync(ct, waypoint);
        }
    }

    private async Task BeforeMoveToTarget(WaypointForTrack waypoint)
    {
        if (waypoint.Action == ActionEnum.UpDownGrabLeaf.Code)
        {
            Simulation.SendInput.Mouse.MiddleButtonClick();
            var screen = CaptureToRectArea();
            var position = await GetPosition(screen, waypoint);
            var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            await _rotateTask.WaitUntilRotatedTo(targetOrientation, 10);
            var handler = ActionFactory.GetBeforeHandler(waypoint.Action);
            await handler.RunAsync(ct, waypoint);
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
            || waypoint.Action == ActionEnum.Fishing.Code
            || waypoint.Action == ActionEnum.ExitAndRelogin.Code
            || waypoint.Action == ActionEnum.SetTime.Code)
        {
            var handler = ActionFactory.GetAfterHandler(waypoint.Action);
            //,PartyConfig
            await handler.RunAsync(ct, waypoint, PartyConfig);
            await Delay(1000, ct);
        }
    }

    private async Task<Avatar?> SwitchAvatar(string index, bool needSkill = false)
    {
        if (string.IsNullOrEmpty(index))
        {
            return null;
        }

        var avatar = _combatScenes?.SelectAvatar(int.Parse(index));
        if (avatar == null) return null;
        if (needSkill && !avatar.IsSkillReady())
        {
            Logger.LogInformation("角色{Name}技能未冷却，跳过。", avatar.Name);
            return null;
        }

        var success = avatar.TrySwitch();
        if (success)
        {
            await Delay(100, ct);
            return avatar;
        }

        Logger.LogInformation("尝试切换角色{Name}失败！", avatar.Name);
        return null;
    }
    
    /// <summary>
    /// 根据时间在两个点之间插值。
    /// </summary>
    /// <param name="startPoint">起点坐标</param>
    /// <param name="endPoint">终点坐标</param>
    /// <param name="startTime">起始时间</param>
    /// <param name="midTime">中间时间</param>
    /// <param name="endTime">结束时间</param>
    /// <returns>中间点坐标</returns>
    public static Point2f InterpolatePointByTime(
        Point2f startPoint,
        Point2f endPoint,
        DateTime startTime,
        DateTime midTime,
        DateTime endTime)
    {
        // 计算时间差
        double totalMillis = (endTime - startTime).TotalMilliseconds;
        double midMillis = (midTime - startTime).TotalMilliseconds;

        // 防止除以0
        if (totalMillis == 0)
            return startPoint;

        // 计算比例
        float t = (float)(midMillis / totalMillis);
        if (t>1.0f)
        {
            t = 1.0f;
        }
        // 插值计算
        float x = startPoint.X + (endPoint.X - startPoint.X) * t;
        float y = startPoint.Y + (endPoint.Y - startPoint.Y) * t;

        return new Point2f(x, y);
    }
    
    private  Point2f prePosition;
    private  DateTime preTime;
    //自动构造点位的最大时间
    private int maxAutoPositionTime=10000; 
    private async Task WaitForCloseMap(int maxAttempts, int delayMs)
    {
        await Delay(delayMs, ct);
        for (var i = 0; i < maxAttempts; i++)
        {
            using var capture = CaptureToRectArea();
            if (Bv.IsInMainUi(capture))
            {
                return;
            }

            await Delay(delayMs, ct);
        }
        
    }

    private async Task<Point2f> GetPosition(ImageRegion imageRegion, WaypointForTrack waypoint)
    {
        return (await GetPositionAndTime(imageRegion, waypoint)).point;
    }
    //
    public bool GetPositionAndTimeSuspendFlag = false;
    private async Task<(Point2f point,int additionalTimeInMs)> GetPositionAndTime(ImageRegion imageRegion, WaypointForTrack waypoint)
    {
        
        var position = Navigation.GetPosition(imageRegion, waypoint.MapName);
        int time = 0;
        if (position == new Point2f())
        {
            if (!Bv.IsInMainUi(imageRegion))
            {
                Logger.LogDebug("小地图位置定位失败，且当前不是主界面，进入异常处理");
                await ResolveAnomalies(imageRegion);
            }
        }

        var distance = Navigation.GetDistance(waypoint, position);
        //中途暂停过，地图未识别到
        if (position is {X:0,Y:0} && GetPositionAndTimeSuspendFlag)
        {
            GetPositionAndTimeSuspendFlag = false;
            throw new RetryNoCountException("可能暂停导致路径过远，重试一次此路线！");
        }
        //何时处理   pathTooFar  路径过远  unrecognized 未识别
        if ((position is {X:0,Y:0} && waypoint.Misidentification.Type.Contains("unrecognized")) || (distance>500 && waypoint.Misidentification.Type.Contains("pathTooFar")))
        {
            if (waypoint.Misidentification.HandlingMode == "previousDetectedPoint")
            {
                if (prePosition != default)
                {
                    position = prePosition;
                    Logger.LogInformation(@$"未识别到具体路径，取上次点位");
                }
            }else if (waypoint.Misidentification.HandlingMode == "mapRecognition"){
                //大地图识别坐标
                DateTime start = DateTime.Now;
                TpTask tpTask = new TpTask(ct);
                await tpTask.OpenBigMapUi();
                try
                {
                    position =MapManager.GetMap(waypoint.MapName).ConvertGenshinMapCoordinatesToImageCoordinates(tpTask.GetPositionFromBigMap(waypoint.MapName));
                }
                catch (Exception e)
                {
                    Logger.LogInformation(@$"地图中心点识别失败！");
                }
               
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                //Bv.IsInMainUi(imageRegion);
                await WaitForCloseMap(10,200);
                DateTime end = DateTime.Now;
                time=(int)(end - start).TotalMilliseconds;
                Logger.LogInformation(@$"未识别到具体路径，打开地图计算中心点({position.X},{position.Y})");
            }
            
            /*if (prePosition!=default)
            {*/
                //position = InterpolatePointByTime(prePosition,new Point2f((float)waypoint.GameX,(float)waypoint.GameY),preTime,DateTime.Now,preTime.AddMilliseconds(maxAutoPositionTime));
                //Logger.LogInformation(@$"未识别到具体路径，预测其路径为（{position.X},{position.Y}）,开始结束点位为：（{prePosition.X},{prePosition.Y}）（{waypoint.GameX},{waypoint.GameY}）");
                //Point2f GetBigMapCenterPoint(string mapName)

               // Logger.LogInformation(@$"未识别到具体路径，打开地图计算中心点({position.X},{position.Y})");
                //position =prePosition;
           // }

        }
        else
        {
            prePosition = position;
            preTime = DateTime.Now;
        }

        //Logger.LogDebug("识别到路径："+position.X+","+position.Y);
        return (position,time);
    }

    /**
     * 处理各种异常场景
     * 需要保证耗时不能太高
     */
    private async Task ResolveAnomalies(ImageRegion? imageRegion = null)
    {
        if (imageRegion == null)
        {
            imageRegion = CaptureToRectArea();
        }

        // 一些异常界面处理
        var cookRa = imageRegion.Find(AutoSkipAssets.Instance.CookRo);
        var closeRa = imageRegion.Find(AutoSkipAssets.Instance.PageCloseMainRo);
        var closeRa2 = imageRegion.Find(ElementAssets.Instance.PageCloseWhiteRo);
        if (cookRa.IsExist() || closeRa.IsExist() || closeRa2.IsExist())
        {
            // 排除大地图
            if (Bv.IsInBigMapUi(imageRegion))
            {
                return;
            }

            Logger.LogInformation("检测到其他界面，使用ESC关闭界面");
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await Delay(1000, ct); // 等待界面关闭
        }


        // 处理月卡
        await _blessingOfTheWelkinMoonTask.Start(ct);

        if (PartyConfig.AutoSkipEnabled)
        {
            // 判断是否进入剧情
            await AutoSkip();
        }
    }

    private async Task AutoSkip()
    {
        var ra = CaptureToRectArea();
        var disabledUiButtonRa = ra.Find(AutoSkipAssets.Instance.DisabledUiButtonRo);
        if (disabledUiButtonRa.IsExist())
        {
            Logger.LogWarning("进入剧情，自动点击剧情直到结束");

            if (_autoSkipTrigger == null)
            {
                _autoSkipTrigger = new AutoSkipTrigger(new AutoSkipConfig
                {
                    Enabled = true,
                    QuicklySkipConversationsEnabled = true, // 快速点击过剧情
                    ClosePopupPagedEnabled = true,
                    ClickChatOption = "优先选择最后一个选项",
                });
                _autoSkipTrigger.Init();
            }

            int noDisabledUiButtonTimes = 0;

            while (true)
            {
                ra = CaptureToRectArea();
                disabledUiButtonRa = ra.Find(AutoSkipAssets.Instance.DisabledUiButtonRo);
                if (disabledUiButtonRa.IsExist())
                {
                    _autoSkipTrigger.OnCapture(new CaptureContent(ra));
                }
                else
                {
                    noDisabledUiButtonTimes++;
                    if (noDisabledUiButtonTimes > 10)
                    {
                        Logger.LogInformation("自动剧情结束");
                        break;
                    }
                }

                await Delay(210, ct);
            }
        }
    }

    private void EndJudgment(ImageRegion ra)
    {
        if (EndAction != null && EndAction(ra))
        {
            throw new NormalEndException("达成结束条件，结束地图追踪");
        }
    }
}