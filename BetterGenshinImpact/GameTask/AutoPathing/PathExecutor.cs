using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.OneDragon;
using BetterGenshinImpact.Helpers;
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
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathExecutor(CancellationToken ct)
{
    private readonly CameraRotateTask _rotateTask = new(ct);
    private readonly TrapEscaper _trapEscaper = new(ct);
    private readonly PathingConfig _config = TaskContext.Instance().Config.PathingConfig;

    private readonly Dictionary<string, string> _actionAvatarIndexMap = new();

    private DateTime _elementalSkillLastUseTime = DateTime.MinValue;

    public async Task Pathing(PathingTask task)
    {
        if (!task.Positions.Any())
        {
            Logger.LogWarning("没有路径点，寻路结束");
            return;
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

        try
        {
            foreach (var waypoint in waypoints)
            {
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
        }
        finally
        {
            _actionAvatarIndexMap.Clear(); // 没啥用，但还是写上
            // 不管咋样，松开所有按键
            Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
            Simulation.SendInput.Mouse.RightButtonUp();
        }
    }

    private void InitializePathing(PathingTask task)
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this,
            "UpdateCurrentPathing", new object(), task));
    }

    private async Task<bool> ValidateGameWithTask(PathingTask task)
    {
        // 校验角色是否存在
        if (task.HasAction("nahida_collect"))
        {
            Logger.LogInformation("此路线存在使用纳西妲收集的动作，校验纳西妲角色是否存在...");

            // 返回主界面再识别
            var returnMainUiTask = new ReturnMainUiTask();
            await returnMainUiTask.Start(ct);

            var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            if (!combatScenes.CheckTeamInitialized())
            {
                Logger.LogError("队伍角色识别失败，无法执行此路径");
                return false;
            }

            var avatar = combatScenes.SelectAvatar("纳西妲");
            if (avatar == null)
            {
                Logger.LogError("队伍中没有纳西妲角色，无法执行此路径");
                return false;
            }

            _actionAvatarIndexMap.Add("nahida_collect", avatar.Index.ToString());
        }

        // 把所有需要切换的角色编号记录下来
        if (task.HasAction("normal_attack"))
        {
            _actionAvatarIndexMap.Add("normal_attack", _config.NormalAttackAvatarIndex);
        }

        if (task.HasAction("elemental_skill"))
        {
            _actionAvatarIndexMap.Add("elemental_skill", _config.ElementalSkillAvatarIndex);
        }

        return true;
    }

    private List<WaypointForTrack> ConvertWaypointsForTrack(List<Waypoint> positions)
    {
        // 把 X Y 转换为 MatX MatY
        return positions.Select(waypoint => new WaypointForTrack(waypoint)).ToList();
    }

    private async Task RecoverWhenLowHp()
    {
        var region = CaptureToRectArea();
        if (Bv.CurrentAvatarIsLowHp(region))
        {
            Logger.LogInformation("当前角色血量过低，去须弥七天神像恢复");
            // tp 到七天神像回血
            var tpTask = new TpTask(ct);
            await tpTask.Tp(TpTask.ReviveStatueOfTheSevenPointX, TpTask.ReviveStatueOfTheSevenPointY, true);
            await Delay(2000, ct);
            Logger.LogInformation("HP恢复完成");
        }
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
        await SwitchAvatar(_config.MainAvatarIndex);

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
                if (distance > 15 != fastMode) // 距离大于30时可以使用疾跑/自由泳
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
                // 使用 E 技能
                if (!string.IsNullOrEmpty(_config.GuardianAvatarIndex) && double.TryParse(_config.GuardianElementalSkillSecondInterval, out var s))
                {
                    if (s < 1)
                    {
                        Logger.LogWarning("元素战技冷却时间设置太短，不执行！");
                        return;
                    }

                    var ms = s * 1000;
                    Debug.WriteLine($"元素战技释放间隔：{(now - _elementalSkillLastUseTime).TotalMilliseconds}ms");
                    if ((now - _elementalSkillLastUseTime).TotalMilliseconds >= ms)
                    {
                        // 可能刚切过人在冷却时间内
                        if (num <= 5 && _config.GuardianAvatarIndex != _config.MainAvatarIndex)
                        {
                            await Delay(800, ct); // 总共1s
                        }

                        await UseElementalSkill();
                        _elementalSkillLastUseTime = now;
                    }
                }

                if (distance > 15)
                {
                    if (Math.Abs((fastModeColdTime - now).TotalMilliseconds) > 2500) //冷却时间2.5s，回复体力用
                    {
                        fastModeColdTime = now;
                        Simulation.SendInput.Mouse.RightButtonClick();
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
        if (string.IsNullOrEmpty(_config.GuardianAvatarIndex))
        {
            return;
        }

        await Delay(200, ct);

        // 切人
        Logger.LogInformation("切换盾、回血角色，使用元素战技");
        await SwitchAvatar(_config.GuardianAvatarIndex);
        if (_config.GuardianElementalSkillLongPress)
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
            if (stepsTaken > 20)
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
            await Delay(50, ct);
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
            || waypoint.Action == ActionEnum.NormalAttack.Code
            || waypoint.Action == ActionEnum.ElementalSkill.Code)
        {
            // 切人
            if (_actionAvatarIndexMap.TryGetValue(waypoint.Action, out var index))
            {
                await SwitchAvatar(index);
            }

            var handler = ActionFactory.GetAfterHandler(waypoint.Action);
            await handler.RunAsync(ct);
            await Delay(1000, ct);
        }
    }

    private async Task SwitchAvatar(string index)
    {
        if (string.IsNullOrEmpty(index))
        {
            return;
        }

        Logger.LogInformation("切换角色{index}", index);
        Simulation.SendInput.Keyboard.KeyPress(User32Helper.ToVk(index));
        await Delay(300, ct);
    }
}
