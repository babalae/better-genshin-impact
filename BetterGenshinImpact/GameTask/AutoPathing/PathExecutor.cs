using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoSkip;
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
using BetterGenshinImpact.Core.Recognition;
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

namespace BetterGenshinImpact.GameTask.AutoPathing;

public partial class PathExecutor
{
    private readonly CameraRotateTask _rotateTask;
    private readonly TrapEscaper _trapEscaper;
    private readonly BlessingOfTheWelkinMoonTask _blessingOfTheWelkinMoonTask = new();
    private AutoSkipTrigger? _autoSkipTrigger;
    public int SuccessFight = 0;
    //路径追踪完全走完所有路径结束的标识
    public bool SuccessEnd = false;
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
        get => _partyConfig ??= PathingPartyConfig.BuildDefault();
        set => _partyConfig = value;
    }

    /// <summary>
    /// 判断是否中止地图追踪的条件
    /// </summary>
    public Func<ImageRegion, bool>? EndAction { get; set; }

    private CombatScenes? _combatScenes;
    private WaypointForTrack? _interruptedInstrumentWaypoint;
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

    private static RecognitionObject GetAutoSkipRecognitionObject(string objectName, ImageRegion region)
    {
        return RecognitionAssets.Get("AutoSkip", objectName, region.Width, region.Height);
    }


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


        var waypointsList = ConvertWaypointsForTrack(task.Positions, task);
        var firstQuest = waypointsList[0][0].InstrumentQuest;
        if (firstQuest != null)
        {
            using var beforeSetup = CaptureToRectArea();
            await InstrumentExitRecovery.TryExitAsync(beforeSetup, ct, instrumentOnly: true, quest: firstQuest);
        }

        // 切换队伍。如果先去神像，后续队伍识别发生在异地。
        var (ready, localQuest) = await SwitchPartyBefore(task, firstQuest);
        if (!ready)
        {
            return;
        }

        // 校验路径是否可以执行
        if (!await ValidateGameWithTask(task, localQuest))
        {
            return;
        }

        InitializePathing(task);

        await Delay(100, ct);
        Navigation.WarmUp(task.Info.MapMatchMethod); // 提前加载地图特征点

        foreach (var waypoints in waypointsList) // 按传送点分割的路径
        {
            CurWaypoints = (waypointsList.FindIndex(wps => wps == waypoints), waypoints);
            for (var i = 0; i < RetryTimes; i++)
            {
                try
                {
                    await ResolveAnomalies(waypoints[0]); // 异常场景处理

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
                            if (CurWaypoints.Item1 > 0)
                            {
                                var prevWaypoints = waypointsList[CurWaypoints.Item1 - 1];
                                var prevWaypoint = prevWaypoints[prevWaypoints.Count - 1];
                                if (prevWaypoint.Type == WaypointType.Teleport.Code
                                    || prevWaypoint.Action == ActionEnum.Fight.Code
                                    || prevWaypoint.Action == ActionEnum.NahidaCollect.Code
                                    || prevWaypoint.Action == ActionEnum.PickAround.Code)
                                {
                                    // No delay
                                }
                                else
                                {
                                    await Delay(1000, ct);
                                }
                            }
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
                                //战斗前的节点记录，用于游泳检测回到战斗节点
                                AutoFightTask.FightWaypoint = waypoint.Action == ActionEnum.Fight.Code ? waypoint : null;

                                // 执行 action
                                await AfterMoveToTarget(waypoint);
                            }
                        }
                    }

                    if (waypoints == waypointsList.Last())
                    {
                        SuccessEnd = true;
                    }
                    break;
                }
                catch (HandledException handledException)
                {
                    SuccessEnd = true;
                    break;
                }
                catch (NormalEndException normalEndException) when (normalEndException is not InstrumentExitRecoveryException)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Logger.LogInformation(normalEndException.Message);
                    }

                    if (!RunnerContext.Instance.isAutoFetchDispatch && RunnerContext.Instance.IsContinuousRunGroup)
                    {
                        throw;
                    }
                    else
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
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
                    InputHub.Foreground.Keyboard.KeyUp(User32.VK.VK_W);
                    InputHub.Foreground.Mouse.RightButtonUp();
                    InputHub.Foreground.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
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

    private async Task<(bool Ready, InstrumentQuestRecovery? LocalQuest)> SwitchPartyBefore(PathingTask task, InstrumentQuestRecovery? quest)
    {
        using var ra = CaptureToRectArea();

        // 切换队伍前判断是否全队死亡 // 可能队伍切换失败导致的死亡
        if (Bv.ClickIfInReviveModal(ra))
        {
            await Bv.WaitForMainUi(ct); // 等待主界面加载完成
            Logger.LogInformation("复苏完成");
            await Delay(4000, ct);
            // 血量肯定不满，直接去七天神像回血
            await TpStatueOfTheSeven(quest);
            quest = null;
        }

        if (PartyConfig.SkipPartySwitch)
        {
            return (true, quest);
        }

        var pRaList = ra.FindMulti(RecognitionAssets.Get("AutoFight", "P", ra)); // 判断是否联机
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
                var result = await SwitchParty(partyName, false, quest);
                if (!result.Ready)
                {
                    Logger.LogError("切换队伍失败，无法执行此路径！请检查地图追踪设置！");
                }
                return result;
            }
            else if (!string.IsNullOrEmpty(PartyConfig.PartyName))
            {
                var result = await SwitchParty(PartyConfig.PartyName, PartyConfig.IsVisitStatueBeforeSwitchParty, quest);
                if (!result.Ready)
                {
                    Logger.LogError("切换队伍失败，无法执行此路径！请检查配置组中的地图追踪配置！");
                }
                return result;
            }
        }

        return (true, quest);
    }

    private void InitializePathing(PathingTask task)
    {
        LogScreenResolution();
        InitHurryOnConfig();
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
    /// <param name="forceTp">切换前是否前往七天神像</param>
    /// <returns></returns>
    private async Task<(bool Ready, InstrumentQuestRecovery? LocalQuest)> SwitchParty(string? partyName, bool forceTp, InstrumentQuestRecovery? quest)
    {
        bool success = true;
        if (!string.IsNullOrEmpty(partyName))
        {
            if (RunnerContext.Instance.PartyName == partyName)
            {
                return (success, quest);
            }

            if (forceTp) // 强制传送模式
            {
                await new TpTask(ct).TpToStatueOfTheSeven(quest);
                quest = null;
                success = await new SwitchPartyTask().Start(partyName, ct);
            }
            else // 优先原地切换模式
            {
                try
                {
                    success = await new SwitchPartyTask().Start(partyName, ct, quest);
                }
                catch (PartySetupFailedException)
                {
                    await new TpTask(ct).TpToStatueOfTheSeven(quest);
                    quest = null;
                    success = await new SwitchPartyTask().Start(partyName, ct);
                }
            }

            if (success)
            {
                RunnerContext.Instance.PartyName = partyName;
                RunnerContext.Instance.ClearCombatScenes();
            }
        }

        return (success, quest);
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
    private async Task<bool> ValidateGameWithTask(PathingTask task, InstrumentQuestRecovery? quest)
    {
        _combatScenes = await RunnerContext.Instance.GetCombatScenes(ct, quest);
        if (_combatScenes == null)
        {
            return false;
        }

        // 没有强制配置的情况下，使用地图追踪内的条件配置
        // 必须放在这里，因为要通过队伍识别来得到最终结果
        var pathingConditionConfig = TaskContext.Instance().Config.PathingConditionConfig;
        var skipPartySwitch = PartyConfig.SkipPartySwitch;
        if (PartyConfig is { Enabled: false })
        {
            PartyConfig = pathingConditionConfig.BuildPartyConfigByCondition(_combatScenes);
            PartyConfig.SkipPartySwitch = skipPartySwitch;
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
            { ActionEnum.AnemoCollect.Code, ElementalType.Anemo },
            { ActionEnum.PyroCollect.Code, ElementalType.Pyro }
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
            WaypointForTrack wft = new WaypointForTrack(waypoint, task.Info.MapName, task.Info.MapMatchMethod);
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
    private async Task<bool> TryPartyHealing(WaypointForTrack waypoint)
    {
        if (_combatScenes is null) return false;
        foreach (var avatar in _combatScenes.GetAvatars())
        {
            if (avatar.Name == "白术")
            {
                if (await TrySwitchWaypointAvatar(avatar, waypoint))
                {
                    //1命白术能两次
                    InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                    await Delay(800, ct);
                    if (!await ConfirmWaypointAvatar(avatar, waypoint)) return false;
                    InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                    await Delay(800, ct);
                    await SwitchAvatar(PartyConfig.MainAvatarIndex, waypoint);
                    await Delay(4000, ct);
                    return true;
                }

                break;
            }
            else if (avatar.Name == "希格雯")
            {
                if (await TrySwitchWaypointAvatar(avatar, waypoint))
                {
                    InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                    await Delay(11000, ct);
                    await SwitchAvatar(PartyConfig.MainAvatarIndex, waypoint);
                    return true;
                }

                break;
            }
            else if (avatar.Name == "珊瑚宫心海")
            {
                if (await TrySwitchWaypointAvatar(avatar, waypoint))
                {
                    InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                    await Delay(500, ct);
                    //尝试Q全队回血
                    if (!await ConfirmWaypointAvatar(avatar, waypoint)) return false;
                    InputHub.Foreground.SimulateAction(GIActions.ElementalBurst);
                    //单人血只给行走位加血
                    await SwitchAvatar(PartyConfig.MainAvatarIndex, waypoint);
                    await Delay(5000, ct);
                    return true;
                }
            }
        }


        return false;
    }

    private async Task RecoverWhenLowHp(WaypointForTrack waypoint)
    {
        var timing = PartyConfig.RecoverTiming;
        if (timing == RecoverTiming.Never)
        {
            return;
        }

        if (timing == RecoverTiming.OnlyTeleport && waypoint.Type != WaypointType.Teleport.Code)
        {
            return;
        }

        using var region = CaptureToRectArea();
        if (Bv.CurrentAvatarIsLowHp(region) && !(await TryPartyHealing(waypoint) && Bv.CurrentAvatarIsLowHp(region)))
        {
            Logger.LogInformation("当前角色血量过低，去七天神像恢复");
            await TpStatueOfTheSeven(waypoint.InstrumentQuest);
            throw new RetryException("回血完成后重试路线");
        }
        else if (Bv.ClickIfInReviveModal(region))
        {
            await Bv.WaitForMainUi(ct); // 等待主界面加载完成
            Logger.LogInformation("复苏完成");
            await Delay(4000, ct);
            // 血量肯定不满，直接去七天神像回血
            await TpStatueOfTheSeven(waypoint.InstrumentQuest);
            throw new RetryException("回血完成后重试路线");
        }
    }

    private async Task TpStatueOfTheSeven(InstrumentQuestRecovery? departureQuest)
    {
        // tp 到七天神像回血
        var tpTask = new TpTask(ct);
        await RunnerContext.Instance.StopAutoPickRunTask(async () => await tpTask.TpToStatueOfTheSeven(departureQuest), 5);
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
        if ( _combatScenes?.CurrentMultiGameStatus?.IsInMultiGame == true || (DateTime.UtcNow - _lastGetExpeditionRewardsTime).TotalMinutes < 5)
        {
            return false;
        }

        //打开大地图操作
        await tpTask.OpenBigMapUi();
        bool changeBigMap = false;
        string adventurersGuildCountry =
            TaskContext.Instance().Config.OtherConfig.AutoFetchDispatchAdventurersGuildCountry;
        if (!RunnerContext.Instance.isAutoFetchDispatch && adventurersGuildCountry != "无" && !string.IsNullOrEmpty(adventurersGuildCountry))
        {
            using var ra1 = CaptureToRectArea();
            var textRect = new Rect(60, 20, 160, 260);
            using var textMat = new Mat(ra1.SrcMat, textRect);
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
                catch (Exception e) when (e is not InstrumentExitRecoveryException)
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
        var tpTask = new TpTask(ct) { InstrumentQuest = waypoint.InstrumentQuest };
        await TryGetExpeditionRewardsDispatch(tpTask);
        var (tpX, tpY) = await tpTask.Tp(waypoint.GameX, waypoint.GameY, waypoint.MapName, forceTp);
        var (tprX, tprY) = MapManager.GetMap(waypoint.MapName, waypoint.MapMatchMethod)
            .ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f((float)tpX, (float)tpY));
        Navigation.SetPrevPosition(tprX, tprY); // 通过上一个位置直接进行局部特征匹配
        await Delay(500, ct); // 多等一会
    }

    public async Task FaceTo(WaypointForTrack waypoint)
    {
        Point2f position;
        using (var screen = CaptureToRectArea())
        {
            position = await GetPosition(screen, waypoint);
        }
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogDebug("朝向点，位置({x2},{y2})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        await WaitUntilRotatedTo(waypoint, targetOrientation, 2);
        await Delay(500, ct);
    }

    public DateTime moveToStartTime;

    public async Task MoveTo(WaypointForTrack waypoint)
    {
        // 切人
        await SwitchAvatar(PartyConfig.MainAvatarIndex, waypoint);
        // 切人完成时刻：切人后有约1秒CD，期间无法切换到其他角色（用于生存位）
        var switchAvatarTime = DateTime.UtcNow;

        Point2f position;
        int additionalTimeInMs;
        using (var initialScreen = CaptureToRectArea())
        {
            (position, additionalTimeInMs, _) = await GetPositionAndTime(initialScreen, waypoint);
        }
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogDebug("粗略接近途经点，位置({x2},{y2})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        await WaitUntilRotatedTo(waypoint, targetOrientation, 5);
        moveToStartTime = DateTime.UtcNow;
        var lastPositionRecord = DateTime.UtcNow;
        var fastMode = false;
        var prevPositions = new List<Point2f>();
        var fastModeColdTime = DateTime.MinValue;
        var prevNotTooFarPosition = position;
        int num = 0, distanceTooFarRetryCount = 0, consecutiveRotationCountBeyondAngle = 0;
        // 连续偏角>5°持续状态的起始时间（配合帧数下限使用，替代原纯帧计数）
        DateTime beyondAngleStartTime = DateTime.MinValue;
        var hurryOnState = new HurryOnState();

        // 赶路帧间隔：始终使用配置值（5-150 钳制），不依赖是否配置赶路角色（空选也可用）
        // 选瓦雷莎时钳制上限 50ms：夜魂疾冲的 E 窗口检测依赖高频帧，间隔再大也不生效
        var hurryFrameIntervalUpper = _hurryOnAvatar == "瓦雷莎" ? 50 : 150;
        var hurryFrameInterval = Math.Clamp(PartyConfig.HurryOnFrameInterval, 5, hurryFrameIntervalUpper);
        while (!ct.IsCancellationRequested)
        {
            num++;
            if ((DateTime.UtcNow - moveToStartTime).TotalSeconds > 240)
            {
                Logger.LogWarning("执行超时，放弃此次追踪");
                throw new RetryException("路径点执行超时，放弃整条路径");
            }

            using var screen = CaptureToRectArea();

            EndJudgment(screen);

            bool recoveredInstrument;
            (position, additionalTimeInMs, recoveredInstrument) = await GetPositionAndTime(screen, waypoint);
            if (recoveredInstrument)
            {
                moveToStartTime = moveToStartTime.AddMilliseconds(additionalTimeInMs);
                targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
                await WaitUntilRotatedTo(waypoint, targetOrientation, 5);
                lastPositionRecord = DateTime.UtcNow;
                prevPositions.Clear();
                distanceTooFarRetryCount = 0;
                consecutiveRotationCountBeyondAngle = 0;
                beyondAngleStartTime = DateTime.MinValue;
                continue;
            }
            // 恢复后的下一帧仍可能再次进入演奏，先检查当前画面再按 W。
            if (!InputHub.Foreground.IsKeyDown(GIActions.MoveForward.ToActionKey().ToVK()))
            {
                InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            }
            if (additionalTimeInMs > 0)
            {
                additionalTimeInMs += 1000; // 起步补偿
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
                    distanceTooFarRetryCount++;
                    if (distanceTooFarRetryCount > 50)
                    {
                        if (position == new Point2f())
                        {
                            throw new HandledException("重试多次后，当前点位无法被识别，放弃此路径！");
                        }
                        else
                        {
                            Logger.LogWarning($"距离过远（{position.X},{position.Y}）->（{waypoint.X},{waypoint.Y}）={distance}，重试多次后仍然失败，放弃此路径点！");
                            throw new HandledException("目标距离过远，可能是当前点位无法识别，放弃此路径！");
                        }
                    }
                    else
                    {
                        // 取余减少日志输出频率
                        if (distanceTooFarRetryCount % 5 == 0)
                        {
                            Logger.LogWarning($"距离过远（{position.X},{position.Y}）->（{waypoint.X},{waypoint.Y}）={distance}，重试");
                        }
                        // 取余减少判断频率
                        if (distanceTooFarRetryCount % 10 == 0)
                        {
                            if (await ResolveAnomalies(waypoint, screen))
                            {
                                var restored = await WaitForInstrumentPosition(waypoint);
                                targetOrientation = Navigation.GetTargetOrientation(waypoint, restored);
                                await WaitUntilRotatedTo(waypoint, targetOrientation, 5);
                                moveToStartTime = DateTime.UtcNow;
                                lastPositionRecord = moveToStartTime;
                                prevPositions.Clear();
                                distanceTooFarRetryCount = 0;
                                consecutiveRotationCountBeyondAngle = 0;
                                beyondAngleStartTime = DateTime.MinValue;
                                continue;
                            }
                            Logger.LogInformation($"重置到上次正确识别的坐标 ({prevNotTooFarPosition.X},{prevNotTooFarPosition.Y})");
                            Navigation.SetPrevPosition(prevNotTooFarPosition.X, prevNotTooFarPosition.Y);
                            // 淡入淡出特效
                            await Delay(500, ct);
                        }
                        await Delay(50, ct);
                        continue;
                    }
                }
            } else
            {
                prevNotTooFarPosition = position;
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
                            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                            Logger.LogInformation("卡死脱离结束");
                            continue;
                        }
                    }
                }
            }

            // 旋转视角
            targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            //执行旋转
            var diff = _rotateTask.RotateToApproach(targetOrientation, screen);
            // 进入MoveTo超过2秒且至少5帧后才启用旋转纠正（绕过起步阶段的抖动）
            if ((DateTime.UtcNow - moveToStartTime).TotalSeconds > 2 && num >= 5)
            {
                if (Math.Abs(diff) > 5)
                {
                    consecutiveRotationCountBeyondAngle++;
                    if (beyondAngleStartTime == DateTime.MinValue)
                    {
                        beyondAngleStartTime = DateTime.UtcNow;
                    }
                }
                else
                {
                    consecutiveRotationCountBeyondAngle = 0;
                    beyondAngleStartTime = DateTime.MinValue;
                }

                // 连续偏角>5°持续超过2秒（且至少3帧）时，说明边走边转不动，松W站定转向
                if (consecutiveRotationCountBeyondAngle >= 3
                    && (DateTime.UtcNow - beyondAngleStartTime).TotalSeconds > 2)
                {
                    // 松W键，站定好转向，转完重新按下W继续走
                    InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    await WaitUntilRotatedTo(waypoint, targetOrientation, 2);
                    if (VerifyMovementScreen(waypoint)) continue; // 下一轮用转向后的新画面决定是否按 W
                    InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                }
            }

            // 赶路逻辑（使用角色技能加速赶路）
            var hurryOnResult = await TryHurryOnAsync(diff, waypoint, distance, screen, num, hurryOnState);
            if (hurryOnResult)
            {
                // continue 会跳过底部 await Delay(...)，
                // 导致 async state machine 的 MoveNext() 永不返回，调用栈逐轮叠加直到溢出。
                // 在此处显式等待以展开栈。
                await Delay(hurryFrameInterval, ct);
                continue;
            }

            // 根据指定方式进行移动
            if (waypoint.MoveMode == MoveModeEnum.Fly.Code)
            {
                var isFlying = Bv.GetMotionStatus(screen) == MotionStatus.Fly;
                if (!isFlying)
                {
                    Debug.WriteLine("未进入飞行状态，按下空格");
                    InputHub.Foreground.SimulateAction(GIActions.Jump);
                    await Delay(200, ct);
                }

                await Delay(100, ct);
                continue;
            }

            if (waypoint.MoveMode == MoveModeEnum.Jump.Code)
            {
                InputHub.Foreground.SimulateAction(GIActions.Jump);
                await Delay(200, ct);
                continue;
            }

            // 只有设置为run才会一直疾跑
            if (waypoint.MoveMode == MoveModeEnum.Run.Code)
            {
                // 演奏恢复可能在定位或站定转向时抬起疾跑键。
                fastMode = fastMode && InputHub.Foreground.IsKeyDown(GIActions.SprintMouse.ToActionKey().ToVK());
                if (distance > 20 != fastMode) // 距离大于20时可以使用疾跑/自由泳
                {
                    if (fastMode)
                    {
                        InputHub.Foreground.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
                    }
                    else
                    {
                        InputHub.Foreground.SimulateAction(GIActions.SprintMouse, KeyType.KeyDown);
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
                        InputHub.Foreground.SimulateAction(GIActions.SprintMouse);
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
                        if ((DateTime.UtcNow - switchAvatarTime).TotalSeconds < 1 &&
                            (!string.IsNullOrEmpty(PartyConfig.MainAvatarIndex) &&
                             PartyConfig.GuardianAvatarIndex != PartyConfig.MainAvatarIndex))
                        {
                            await Delay(800, ct); // 总共1s
                        }

                        await UseElementalSkill(waypoint);
                        _elementalSkillLastUseTime = DateTime.UtcNow;
                        if (VerifyMovementScreen(waypoint)) continue;
                    }
                }

                // 自动疾跑
                if (distance > 20 && PartyConfig.AutoRunEnabled)
                {
                    if (Math.Abs((fastModeColdTime - DateTime.UtcNow).TotalMilliseconds) > 2500) //冷却时间2.5s，回复体力用
                    {
                        fastModeColdTime = DateTime.UtcNow;
                        InputHub.Foreground.SimulateAction(GIActions.SprintMouse);
                    }
                }
            }

            // 使用小道具
            if (PartyConfig.UseGadgetIntervalMs > 0)
            {
                if ((DateTime.UtcNow - _useGadgetLastUseTime).TotalMilliseconds > PartyConfig.UseGadgetIntervalMs)
                {
                    InputHub.Foreground.SimulateAction(GIActions.QuickUseGadget);
                    _useGadgetLastUseTime = DateTime.UtcNow;
                }
            }

            await Delay(hurryFrameInterval, ct);
        }

        // 抬起w键
        InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
    }

    private async Task UseElementalSkill(WaypointForTrack waypoint)
    {
        if (string.IsNullOrEmpty(PartyConfig.GuardianAvatarIndex))
        {
            return;
        }

        await Delay(200, ct);

        // 切人
        Logger.LogInformation("切换盾、回血角色，使用元素战技");
        var avatar = await SwitchAvatar(PartyConfig.GuardianAvatarIndex, waypoint, true);
        if (avatar == null)
        {
            return;
        }

        // 钟离往身后放柱子
        if (avatar.Name == "钟离")
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            await Delay(50, ct);
            if (!await ConfirmWaypointAvatar(avatar, waypoint)) return;
            InputHub.Foreground.SimulateAction(GIActions.MoveBackward);
            await Delay(200, ct);
        }

        if (!await ConfirmWaypointAvatar(avatar, waypoint)) return;
        avatar.UseSkill(PartyConfig.GuardianElementalSkillLongPress);

        // 钟离往身后放柱子 后继续走路
        if (avatar.Name == "钟离" && !VerifyMovementScreen(waypoint))
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        }
    }

    private async Task MoveCloseTo(WaypointForTrack waypoint)
    {
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

            using var screen = CaptureToRectArea();

            EndJudgment(screen);

            position = await GetPosition(screen, waypoint);
            if (Navigation.GetDistance(waypoint, position) < 2)
            {
                Logger.LogDebug("已到达路径点");
                break;
            }

            targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            await WaitUntilRotatedTo(waypoint, targetOrientation, 2);
            // 小碎步接近
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            Thread.Sleep(60);
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            // InputHub.Foreground.Keyboard.KeyDown(User32.VK.VK_W).Sleep(60).KeyUp(User32.VK.VK_W);
            await Delay(20, ct);
        }

        InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);

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
            if (VerifyMovementScreen(waypoint))
                await RunnerContext.EnsureWaypointActionWorldAsync(ct, waypoint.InstrumentQuest);
            InputHub.Foreground.Mouse.MiddleButtonClick();
            await Delay(300, ct);
            using var screen = CaptureToRectArea();
            var position = await GetPosition(screen, waypoint);
            var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            await WaitUntilRotatedTo(waypoint, targetOrientation, 10);
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
            await handler.RunAsync(ct, waypoint, PartyConfig);
            //统计结束战斗的次数
            if (waypoint.Action == ActionEnum.Fight.Code)
            {
                SuccessFight++;
            }
            await Delay(1000, ct);
        }
    }

    private Task<bool> TrySwitchWaypointAvatar(Avatar avatar, WaypointForTrack waypoint, int tries = 4) =>
        RunnerContext.TrySwitchWaypointAvatarAsync(avatar, tries, ct, waypoint.InstrumentQuest, VerifyMovementScreen(waypoint));

    private async Task<bool> ConfirmWaypointAvatar(Avatar avatar, WaypointForTrack waypoint) =>
        !VerifyMovementScreen(waypoint) || !await RunnerContext.EnsureWaypointActionWorldAsync(ct, waypoint.InstrumentQuest)
        || await TrySwitchWaypointAvatar(avatar, waypoint);

    private async Task<Avatar?> SwitchAvatar(string index, WaypointForTrack? waypoint, bool needSkill = false)
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

        var success = waypoint == null ? avatar.TrySwitch(5) : await TrySwitchWaypointAvatar(avatar, waypoint, 5);
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
    private async Task<(Point2f point, int additionalTimeInMs, bool recoveredInstrument)> GetPositionAndTime(
        ImageRegion imageRegion, WaypointForTrack waypoint)
    {
        if (VerifyMovementScreen(waypoint) && !IsMovementWorld(imageRegion))
        {
            var started = Stopwatch.GetTimestamp();
            await RestoreMovementWorld(waypoint, imageRegion);
            return await RelocateAfterInstrument(waypoint, started);
        }

        var position = Navigation.GetPosition(imageRegion, waypoint.MapName, waypoint.MapMatchMethod);
        int time = 0;
        if (position == new Point2f())
        {
            if (!Bv.IsInMainUi(imageRegion))
            {
                Logger.LogDebug("小地图位置定位失败，且当前不是主界面，进入异常处理");
                var recoveryStarted = Stopwatch.GetTimestamp();
                if (await ResolveAnomalies(waypoint, imageRegion))
                {
                    return await RelocateAfterInstrument(waypoint, recoveryStarted);
                }
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
                var mapStarted = Stopwatch.GetTimestamp();
                var tpTask = new TpTask(ct) { InstrumentQuest = waypoint.InstrumentQuest };
                await tpTask.OpenBigMapUi(mapName: waypoint.MapName);
                try
                {
                    position =MapManager.GetMap(waypoint.MapName, waypoint.MapMatchMethod).ConvertGenshinMapCoordinatesToImageCoordinates(tpTask.GetPositionFromBigMap(waypoint.MapName));
                }
                catch (Exception e)
                {
                    Logger.LogInformation(@$"地图中心点识别失败！");
                }
               
                var closeMap = true;
                if (VerifyMovementScreen(waypoint))
                {
                    using var beforeClose = CaptureToRectArea();
                    closeMap = Bv.IsInBigMapUi(beforeClose);
                    if (!closeMap && !IsMovementWorld(beforeClose))
                    {
                        await RestoreMovementWorld(waypoint, beforeClose);
                        return await RelocateAfterInstrument(waypoint, mapStarted);
                    }
                }
                if (closeMap)
                {
                    InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                    await WaitForCloseMap(10,200);
                }
                if (VerifyMovementScreen(waypoint))
                {
                    using var afterMap = CaptureToRectArea();
                    if (!IsMovementWorld(afterMap))
                    {
                        await RestoreMovementWorld(waypoint, afterMap);
                        return await RelocateAfterInstrument(waypoint, mapStarted);
                    }
                }
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
        return (position, time, false);
    }

    private async Task<(Point2f, int, bool)> RelocateAfterInstrument(WaypointForTrack waypoint, long started)
    {
        var position = await WaitForInstrumentPosition(waypoint);
        var elapsed = (int)Math.Min(int.MaxValue, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return (position, elapsed, true);
    }

    private bool VerifyMovementScreen(WaypointForTrack waypoint) =>
        waypoint.InstrumentQuest != null || ReferenceEquals(waypoint, _interruptedInstrumentWaypoint);

    private static bool IsMovementWorld(ImageRegion screen) => RunnerContext.IsWaypointActionWorld(screen);

    private async Task RestoreMovementWorld(WaypointForTrack waypoint, ImageRegion screen)
    {
        if (!await TryRecoverInstrument(waypoint, screen))
            await RunnerContext.EnsureWaypointActionWorldAsync(ct, waypoint.InstrumentQuest);
    }

    private async Task<bool> TryRecoverInstrument(WaypointForTrack waypoint, ImageRegion screen)
    {
        if (!await InstrumentExitRecovery.TryExitAsync(screen, ct, instrumentOnly: true, quest: waypoint.InstrumentQuest))
            return false;
        _interruptedInstrumentWaypoint = waypoint;
        return true;
    }

    private async Task<Point2f> WaitForInstrumentPosition(WaypointForTrack waypoint)
    {
        Point2f position = default;
        await InstrumentExitRecovery.WaitForWorldAsync(TaskContext.Instance().Runtime, "等待恢复当前路径点定位", ct,
            region =>
            {
                position = Navigation.GetPosition(region, waypoint.MapName, waypoint.MapMatchMethod);
                return position != default;
            }, reentry: waypoint.InstrumentQuest);
        return position;
    }

    private async Task WaitUntilRotatedTo(WaypointForTrack waypoint, int targetOrientation, int maxDiff)
    {
        var secondAttempt = false;
        while (true)
        {
            var guarded = VerifyMovementScreen(waypoint);
            bool interrupted = false;
            bool CanRotate(ImageRegion screen)
            {
                interrupted = !IsMovementWorld(screen);
                return !interrupted;
            }
            if (await _rotateTask.WaitUntilRotatedTo(targetOrientation, maxDiff, guarded ? CanRotate : null))
                return;

            ct.ThrowIfCancellationRequested();
            var recoveryStarted = Stopwatch.GetTimestamp();
            using var current = CaptureToRectArea();
            if (guarded)
            {
                bool world = IsMovementWorld(current);
                if (world && !interrupted)
                {
                    if (secondAttempt) return;
                    secondAttempt = true;
                    continue;
                }
                if (!world) await RestoreMovementWorld(waypoint, current);
            }
            else if (secondAttempt)
            {
                // 普通转向仍按原有的两轮尝试结束；第二轮中途进入演奏则继续恢复这个点。
                if (!await TryRecoverInstrument(waypoint, current)) return;
            }
            else if (!await ResolveAnomalies(waypoint, current))
            {
                secondAttempt = true;
                continue;
            }

            targetOrientation = Navigation.GetTargetOrientation(waypoint, await WaitForInstrumentPosition(waypoint));
            secondAttempt = false;
            if (moveToStartTime != default)
                moveToStartTime += Stopwatch.GetElapsedTime(recoveryStarted);
        }
    }

    /**
     * 处理各种异常场景
     * 需要保证耗时不能太高
     */
    private async Task<bool> ResolveAnomalies(WaypointForTrack waypoint, ImageRegion? imageRegion = null)
    {
        using var ownedImageRegion = imageRegion == null ? CaptureToRectArea() : null;
        imageRegion ??= ownedImageRegion!;

        if (await TryRecoverInstrument(waypoint, imageRegion))
            return true;

        if (VerifyMovementScreen(waypoint))
        {
            if (IsMovementWorld(imageRegion)) return false;
            await RunnerContext.EnsureWaypointActionWorldAsync(ct, waypoint.InstrumentQuest);
            return true;
        }

        // 一些异常界面处理
        var cookRa = imageRegion.Find(GetAutoSkipRecognitionObject("Cook", imageRegion));
        var closeRa = imageRegion.Find(GetAutoSkipRecognitionObject("PageCloseMain", imageRegion));
        var closeRa2 = imageRegion.Find(ElementRecognition.Get("PageCloseWhite", imageRegion));
        var closeRa3 = imageRegion.Find(GetAutoSkipRecognitionObject("PageClose", imageRegion));
        if (cookRa.IsExist() || closeRa.IsExist() || closeRa2.IsExist() || closeRa3.IsExist())
        {
            // 排除大地图
            if (Bv.IsInBigMapUi(imageRegion))
            {
                return false;
            }

            Logger.LogInformation("检测到其他界面，使用ESC关闭界面");
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await Delay(1000, ct); // 等待界面关闭
        }


        // 处理月卡
        await _blessingOfTheWelkinMoonTask.Start(ct);

        if (PartyConfig.AutoSkipEnabled)
        {
            // 判断是否进入剧情
            await AutoSkip();
        }
        return false;
    }

    private async Task AutoSkip()
    {
        using var ra = CaptureToRectArea();
        var disabledUiButtonRa = ra.Find(GetAutoSkipRecognitionObject("DisabledUiButton", ra));
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
                // 这个实例由路径追踪自己驱动，不经过调度器，需要手动进入启用状态
                _autoSkipTrigger.OnEnabled(null);
            }

            int noDisabledUiButtonTimes = 0;

            while (true)
            {
                using var captureContent = new CaptureContent(CaptureToRectArea());
                var currentCapture = captureContent.CaptureRectArea;
                disabledUiButtonRa = currentCapture.Find(GetAutoSkipRecognitionObject("DisabledUiButton", currentCapture));
                if (disabledUiButtonRa.IsExist())
                {
                    _autoSkipTrigger.OnCapture(captureContent);
                    noDisabledUiButtonTimes = 0;
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
            throw new HandledException("达成结束条件，结束地图追踪");
        }
    }
}
