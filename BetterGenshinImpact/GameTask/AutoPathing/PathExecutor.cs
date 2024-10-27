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
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathExecutor(CancellationToken ct)
{
    private readonly CameraRotateTask _rotateTask = new(ct);
    private readonly TrapEscaper _trapEscaper = new(ct);

    private PathingConfig? _config;

    public PathingConfig Config
    {
        get => _config ??= new PathingConfig();
        set => _config = value;
    }

    private CombatScenes? _combatScenes;
    private readonly Dictionary<string, string> _actionAvatarIndexMap = new();

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
                _actionAvatarIndexMap.Clear(); // 没啥用，但还是写上
                // 不管咋样，松开所有按键
                Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
                Simulation.SendInput.Mouse.RightButtonUp();
            }
        }
    }

    private void InitializePathing(PathingTask task)
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this,
            "UpdateCurrentPathing", new object(), task));
    }

    private async Task<bool> ValidateGameWithTask(PathingTask task)
    {
        _combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
        if (_combatScenes == null)
        {
            return false;
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

            _actionAvatarIndexMap.Add("nahida_collect", avatar.Index.ToString());
        }

        // 把所有需要切换的角色编号记录下来
        if (task.HasAction("normal_attack"))
        {
            if (string.IsNullOrEmpty(Config.NormalAttackAvatarIndex))
            {
                Logger.LogError("此路径存在普攻动作，未设置普攻角色编号，无法执行此路径！");
                return false;
            }

            _actionAvatarIndexMap.Add("normal_attack", Config.NormalAttackAvatarIndex);
        }

        if (task.HasAction("elemental_skill"))
        {
            if (string.IsNullOrEmpty(Config.ElementalSkillAvatarIndex))
            {
                Logger.LogError("此路径存在释放元素战技动作，未设置元素战技角色编号，无法执行此路径！");
                return false;
            }

            _actionAvatarIndexMap.Add("elemental_skill", Config.ElementalSkillAvatarIndex);
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
            await Delay(3000, ct);
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
        await SwitchAvatar(Config.MainAvatarIndex);

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
                if (!string.IsNullOrEmpty(Config.GuardianAvatarIndex) && double.TryParse(Config.GuardianElementalSkillSecondInterval, out var s))
                {
                    if (s < 1)
                    {
                        Logger.LogWarning("元素战技冷却时间设置太短，不执行！");
                        return;
                    }

                    var ms = s * 1000;
                    Debug.WriteLine($"元素战技释放间隔：{(now - _elementalSkillLastUseTime).TotalMilliseconds}ms");
                    if ((now - _elementalSkillLastUseTime).TotalMilliseconds > ms)
                    {
                        // 可能刚切过人在冷却时间内
                        if (num <= 5 && (!string.IsNullOrEmpty(Config.MainAvatarIndex) && Config.GuardianAvatarIndex != Config.MainAvatarIndex))
                        {
                            await Delay(800, ct); // 总共1s
                        }

                        await UseElementalSkill();
                        _elementalSkillLastUseTime = now;
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
        if (string.IsNullOrEmpty(Config.GuardianAvatarIndex))
        {
            return;
        }

        await Delay(200, ct);

        // 切人
        Logger.LogInformation("切换盾、回血角色，使用元素战技");
        var avatar = await SwitchAvatar(Config.GuardianAvatarIndex);
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

        if (Config.GuardianElementalSkillLongPress)
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
                var avatar = await SwitchAvatar(index);
                if (avatar == null)
                {
                    return;
                }
            }

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
}
