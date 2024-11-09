using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Map;
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
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using ActionEnum = BetterGenshinImpact.GameTask.AutoPathing.Model.Enum.ActionEnum;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathExecutor(CancellationToken ct)
{
    private readonly CameraRotateTask _rotateTask = new(ct);
    private readonly TrapEscaper _trapEscaper = new(ct);
    private readonly BlessingOfTheWelkinMoonTask _blessingOfTheWelkinMoonTask = new();
    private AutoSkipTrigger? _autoSkipTrigger;

    private PathingPartyConfig? _partyConfig;

    public PathingPartyConfig PartyConfig
    {
        get => _partyConfig ?? new PathingPartyConfig();
        set => _partyConfig = value;
    }

    private CombatScenes? _combatScenes;
    // private readonly Dictionary<string, string> _actionAvatarIndexMap = new();

    private DateTime _elementalSkillLastUseTime = DateTime.MinValue;

    private const int RetryTimes = 2;
    private int _inTrap = 0;

    public async Task Pathing(PathingTask task)
    {
        if (!task.Positions.Any())
        {
            Logger.LogWarning("没有路径点，寻路结束");
            return;
        }

        // 切换队伍
        if (PartyConfig is { Enabled: false })
        {
            // 调度器未配置的情况下，根据路径追踪条件配置切换队伍
            var partyName = FilterPartyNameByConditionConfig(task);
            if (!await SwitchParty(partyName))
            {
                Logger.LogError("切换队伍失败，无法执行此路径！请检查路径追踪设置！");
                return;
            }
        }
        else if (!string.IsNullOrEmpty(PartyConfig.PartyName))
        {
            if (!await SwitchParty(PartyConfig.PartyName))
            {
                Logger.LogError("切换队伍失败，无法执行此路径！请检查配置组中的路径追踪配置！");
                return;
            }
        }

        // 校验路径是否可以执行
        if (!await ValidateGameWithTask(task))
        {
            return;
        }

        InitializePathing(task);

        var waypoints = ConvertWaypointsForTrack(task.Positions);

        await Delay(100, ct);
        Navigation.WarmUp(); // 提前加载地图特征点

        for (var i = 0; i < RetryTimes; i++)
        {
            try
            {
                foreach (var waypoint in waypoints)
                {
                    await ResolveAnomalies();
                    await RecoverWhenLowHp(); // 低血量恢复
                    if (waypoint.Type == WaypointType.Teleport.Code)
                    {
                        await HandleTeleportWaypoint(waypoint);
                    }
                    else
                    {
                        await BeforeMoveToTarget(waypoint);

                        // Path不用走得很近，Target需要接近，但都需要先移动到对应位置
                        await MoveTo(waypoint);

                        if (waypoint.Type == WaypointType.Target.Code || !string.IsNullOrEmpty(waypoint.Action))
                        {
                            await MoveCloseTo(waypoint);
                            // 到达点位后执行 action
                            await AfterMoveToTarget(waypoint);
                        }
                    }
                }

                break;
            }
            catch (RetryException retryException)
            {
                Logger.LogWarning(retryException.Message);
            }
            finally
            {
                // 不管咋样，松开所有按键
                Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
                Simulation.SendInput.Mouse.RightButtonUp();
                await ResolveAnomalies();
            }
        }
    }

    private void InitializePathing(PathingTask task)
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this,
            "UpdateCurrentPathing", new object(), task));
    }

    /// <summary>
    /// 切换队伍
    /// </summary>
    /// <param name="partyName"></param>
    /// <returns></returns>
    private async Task<bool> SwitchParty(string? partyName)
    {
        if (!string.IsNullOrEmpty(partyName))
        {
            if (RunnerContext.Instance.PartyName == partyName)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(RunnerContext.Instance.PartyName))
            {
                // 非空的情况下，先tp到安全位置（回血的七天神像）
                await new TpTask(ct).Tp(TpTask.ReviveStatueOfTheSevenPointX, TpTask.ReviveStatueOfTheSevenPointY, true);
            }

            var success = await new SwitchPartyTask().Start(partyName, ct);
            if (success)
            {
                RunnerContext.Instance.PartyName = partyName;
                RunnerContext.Instance.ClearCombatScenes();
                return true;
            }

            return false;
        }

        return true;
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

        // 没有强制配置的情况下，使用路径追踪内的条件配置
        // 必须放在这里，因为要通过队伍识别来得到最终结果
        var pathingConditionConfig = TaskContext.Instance().Config.PathingConditionConfig;
        if (PartyConfig is { Enabled: false })
        {
            PartyConfig = pathingConditionConfig.BuildPartyConfigByCondition(_combatScenes);
        }

        // 校验角色是否存在
        if (task.HasAction("nahida_collect"))
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

    private bool ValidateElementalActionAvatarIndex(PathingTask task, string action, ElementalType el, CombatScenes combatScenes)
    {
        if (task.HasAction(action))
        {
            foreach (var avatar in combatScenes.Avatars)
            {
                if (ElementalCollectAvatarConfigs.Get(avatar.Name, el) != null)
                {
                    return true;
                }
            }

            Logger.LogError("此路径存在 {action} 收集动作，队伍中没有对应元素角色:{}，无法执行此路径！", action, string.Join(",", ElementalCollectAvatarConfigs.GetAvatarNameList(el)));
            return false;
        }
        else
        {
            return true;
        }
    }

    private List<WaypointForTrack> ConvertWaypointsForTrack(List<Waypoint> positions)
    {
        // 把 X Y 转换为 MatX MatY
        return positions.Select(waypoint => new WaypointForTrack(waypoint)).ToList();
    }

    private async Task RecoverWhenLowHp()
    {
        using var region = CaptureToRectArea();
        if (Bv.CurrentAvatarIsLowHp(region))
        {
            Logger.LogInformation("当前角色血量过低，去须弥七天神像恢复");
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
        await tpTask.Tp(TpTask.ReviveStatueOfTheSevenPointX, TpTask.ReviveStatueOfTheSevenPointY, true);
        await Delay(3000, ct);
        Logger.LogInformation("HP恢复完成");
    }

    private async Task HandleTeleportWaypoint(WaypointForTrack waypoint)
    {
        var forceTp = waypoint.Action == ActionEnum.ForceTp.Code;
        var (tpX, tpY) = await new TpTask(ct).Tp(waypoint.GameX, waypoint.GameY, forceTp);
        var (tprX, tprY) = MapCoordinate.GameToMain2048(tpX, tpY);
        EntireMap.Instance.SetPrevPosition((float)tprX, (float)tprY); // 通过上一个位置直接进行局部特征匹配
        await Delay(500, ct); // 多等一会
    }

    private async Task MoveTo(WaypointForTrack waypoint)
    {
        // 切人
        await SwitchAvatar(PartyConfig.MainAvatarIndex);

        var screen = CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogInformation("粗略接近途经点，位置({x2},{y2})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);
        var startTime = DateTime.UtcNow;
        var lastPositionRecord = DateTime.UtcNow;
        var fastMode = false;
        var prevPositions = new List<Point2f>();
        var fastModeColdTime = DateTime.MinValue;
        var num = 0;

        // 按下w，一直走
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        while (!ct.IsCancellationRequested)
        {
            num++;
            var now = DateTime.UtcNow;
            if ((now - startTime).TotalSeconds > 240)
            {
                Logger.LogWarning("执行超时，跳过路径点");
                break;
            }

            screen = CaptureToRectArea();
            position = Navigation.GetPosition(screen);
            var distance = Navigation.GetDistance(waypoint, position);
            Debug.WriteLine($"接近目标点中，距离为{distance}");
            if (distance < 4)
            {
                Logger.LogInformation("到达路径点附近");
                break;
            }

            if (distance > 500)
            {
                Logger.LogWarning("距离过远，跳过路径点");
                break;
            }

            // 非攀爬状态下，检测是否卡死（脱困触发器）
            if (waypoint.MoveMode != MoveModeEnum.Climb.Code)
            {
                if ((now - lastPositionRecord).TotalMilliseconds > 1000)
                {
                    lastPositionRecord = now;
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
                            Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
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
                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                    await Delay(200, ct);
                }

                continue;
            }

            if (waypoint.MoveMode == MoveModeEnum.Jump.Code)
            {
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                await Delay(200, ct);
                continue;
            }

            // 只有设置为run才会一直疾跑
            if (waypoint.MoveMode == MoveModeEnum.Run.Code)
            {
                if (distance > 20 != fastMode) // 距离大于30时可以使用疾跑/自由泳
                {
                    if (fastMode)
                    {
                        Simulation.SendInput.Mouse.RightButtonUp();
                    }
                    else
                    {
                        Simulation.SendInput.Mouse.RightButtonDown();
                    }

                    fastMode = !fastMode;
                }
            }
            else if (waypoint.MoveMode != MoveModeEnum.Climb.Code) //否则自动短疾跑
            {
                if (distance > 20)
                {
                    if (Math.Abs((fastModeColdTime - now).TotalMilliseconds) > 2500) //冷却时间2.5s，回复体力用
                    {
                        fastModeColdTime = now;
                        Simulation.SendInput.Mouse.RightButtonClick();
                    }
                }

                // 使用 E 技能
                if (!string.IsNullOrEmpty(PartyConfig.GuardianAvatarIndex) && double.TryParse(PartyConfig.GuardianElementalSkillSecondInterval, out var s))
                {
                    if (s < 1)
                    {
                        Logger.LogWarning("元素战技冷却时间设置太短，不执行！");
                        return;
                    }

                    var ms = s * 1000;
                    Debug.WriteLine($"元素战技释放间隔：{(now - _elementalSkillLastUseTime).TotalMilliseconds}ms");
                    if ((DateTime.UtcNow - _elementalSkillLastUseTime).TotalMilliseconds > ms)
                    {
                        // 可能刚切过人在冷却时间内
                        if (num <= 5 && (!string.IsNullOrEmpty(PartyConfig.MainAvatarIndex) && PartyConfig.GuardianAvatarIndex != PartyConfig.MainAvatarIndex))
                        {
                            await Delay(800, ct); // 总共1s
                        }

                        await UseElementalSkill();
                        _elementalSkillLastUseTime = DateTime.UtcNow;
                    }
                }
            }

            await Delay(100, ct);
        }

        // 抬起w键
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
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
        var avatar = await SwitchAvatar(PartyConfig.GuardianAvatarIndex);
        if (avatar == null)
        {
            return;
        }

        // 钟离往身后放柱子
        if (avatar.Name == "钟离")
        {
            Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
            await Delay(50, ct);
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_S);
            await Delay(200, ct);
        }

        if (PartyConfig.GuardianElementalSkillLongPress)
        {
            Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_E);
            await Task.Delay(800); // 不能取消
            Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_E);
            await Delay(700, ct);
        }
        else
        {
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_E);
            await Delay(300, ct);
        }

        // 钟离往身后放柱子 后继续走路
        if (avatar.Name == "钟离")
        {
            Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        }
    }

    private async Task MoveCloseTo(WaypointForTrack waypoint)
    {
        var screen = CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogInformation("精确接近目标点，位置({x2},{y2})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        if (waypoint.MoveMode == MoveModeEnum.Fly.Code && waypoint.Action == ActionEnum.StopFlying.Code)
        {
            //下落攻击接近目的地
            Logger.LogInformation("动作：下落攻击");
            Simulation.SendInput.Mouse.LeftButtonClick();
            await Delay(1000, ct);
        }

        await _rotateTask.WaitUntilRotatedTo(targetOrientation, 2);
        var stepsTaken = 0;
        while (!ct.IsCancellationRequested)
        {
            stepsTaken++;
            if (stepsTaken > 30)
            {
                Logger.LogWarning("精确接近超时");
                break;
            }

            screen = CaptureToRectArea();
            position = Navigation.GetPosition(screen);
            if (Navigation.GetDistance(waypoint, position) < 2)
            {
                Logger.LogInformation("已到达路径点");
                break;
            }

            targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            await _rotateTask.WaitUntilRotatedTo(targetOrientation, 2);
            // 小碎步接近
            Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W).Sleep(60).KeyUp(User32.VK.VK_W);
            await Delay(20, ct);
        }

        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);

        // 到达目的地后停顿一秒
        await Delay(1000, ct);
    }

    private async Task BeforeMoveToTarget(WaypointForTrack waypoint)
    {
        if (waypoint.Action == ActionEnum.UpDownGrabLeaf.Code)
        {
            var handler = ActionFactory.GetBeforeHandler(waypoint.Action);
            await handler.RunAsync(ct);
            await Delay(800, ct);
        }
    }

    private async Task AfterMoveToTarget(Waypoint waypoint)
    {
        if (waypoint.Action == ActionEnum.NahidaCollect.Code
            || waypoint.Action == ActionEnum.PickAround.Code
            || waypoint.Action == ActionEnum.Fight.Code
            || waypoint.Action == ActionEnum.HydroCollect.Code
            || waypoint.Action == ActionEnum.ElectroCollect.Code
            || waypoint.Action == ActionEnum.AnemoCollect.Code)
        {
            var handler = ActionFactory.GetAfterHandler(waypoint.Action);
            await handler.RunAsync(ct);
            await Delay(1000, ct);
        }
    }

    private async Task<Avatar?> SwitchAvatar(string index)
    {
        if (string.IsNullOrEmpty(index))
        {
            return null;
        }

        var avatar = _combatScenes?.Avatars[int.Parse(index) - 1];
        if (avatar != null)
        {
            bool success = avatar.TrySwitch();
            if (success)
            {
                await Delay(100, ct);
                return avatar;
            }
            else
            {
                Logger.LogInformation("尝试切换角色{Name}失败！", avatar.Name);
            }
        }

        return null;
    }

    /**
     * 处理各种异常场景
     * 需要保证耗时不能太高
     */

    public async Task ResolveAnomalies()
    {
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
                _autoSkipTrigger = new AutoSkipTrigger();
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
                    if (noDisabledUiButtonTimes > 50)
                    {
                        Logger.LogInformation("自动剧情结束");
                        break;
                    }
                }

                await Delay(210, ct);
            }
        }
    }
}
