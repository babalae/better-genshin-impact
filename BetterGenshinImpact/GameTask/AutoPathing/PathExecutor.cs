using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
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
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathExecutor(CancellationTokenSource cts)
{
    private double _dpi = 1;

    private CameraRotateTask _rotateTask = new(cts);

    private bool SkipWaypoint = false;

    public async Task Pathing(PathingTask task)
    {
        _dpi = TaskContext.Instance().DpiScale;
        if (!task.Positions.Any())
        {
            Logger.LogWarning("没有路径点，寻路结束");
            return;
        }

        InitializePathing(task);

        var waypoints = ConvertWaypoints(task.Positions);

        await Delay(100, cts);
        Navigation.WarmUp(); // 提前加载地图特征点

        try
        {
            foreach (var waypoint in waypoints)
            {
                if (waypoint.Type == WaypointType.Teleport.Code)
                {
                    await HandleTeleportWaypoint(waypoint);
                }
                else
                {
                    // Path不用走得很近，Target需要接近，但都需要先移动到对应位置
                    await MoveTo(waypoint);
                    // 跳过路径点后，当前路径点不处理
                    if (SkipWaypoint)
                    {
                        SkipWaypoint = false;
                        continue;
                    }

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
            // 不管咋样，松开所有按键
            Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_E);
            Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_SHIFT);
            Simulation.SendInput.Mouse.RightButtonUp();
        }
    }

    private void InitializePathing(PathingTask task)
    {
        task.Positions.First().Type = WaypointType.Teleport.Code;
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this,
            "UpdateCurrentPathing", new object(), task));
    }

    private List<Waypoint> ConvertWaypoints(List<Waypoint> positions)
    {
        var waypoints = new List<Waypoint>();
        foreach (var waypoint in positions)
        {
            if (waypoint.Type == WaypointType.Teleport.Code)
            {
                waypoints.Add(waypoint);
                continue;
            }
            var waypointCopy = new Waypoint
            {
                Action = waypoint.Action,
                Type = waypoint.Type,
                MoveMode = waypoint.MoveMode
            };
            (waypointCopy.X, waypointCopy.Y) = MapCoordinate.GameToMain2048(waypoint.X, waypoint.Y);
            waypoints.Add(waypointCopy);
        }
        return waypoints;
    }

    private async Task HandleTeleportWaypoint(Waypoint waypoint)
    {
        var forceTp = waypoint.Action == ActionEnum.ForceTp.Code;
        var (tpX, tpY) = await new TpTask(cts).Tp(waypoint.X, waypoint.Y, forceTp);
        var (tprX, tprY) = MapCoordinate.GameToMain2048(tpX, tpY);
        EntireMap.Instance.SetPrevPosition((float)tprX, (float)tprY); // 通过上一个位置直接进行局部特征匹配
    }

    private async Task MoveTo(Waypoint waypoint)
    {
        Logger.LogInformation("粗略接近路径点，位置({x2},{y2})", $"{waypoint.X:F1}", $"{waypoint.Y:F1}");
        var screen = CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);

        var hasCharacter = false;
        // TODO 增加识别角色并切换的逻辑
        // 可以考虑放到游泳，攀爬，等移动逻辑中

        var startTime = DateTime.UtcNow;
        var lastPositionRecord = DateTime.UtcNow;
        var prevPositions = new List<Point2f>();
        // 新增逻辑：普通向前移动，疾跑向前移动，飞行向前移动，游泳向前移动，攀爬向前移动，角色技能向前移动,脱离卡死
        // NormalForward
        // SprintForward
        // FlightForward
        // SwimmingForward
        // ClimbForward
        // CharacterSkillForward
        // GetOutOfTheJam

        while (!cts.IsCancellationRequested)
        {
            screen = CaptureToRectArea();
            position = Navigation.GetPosition(screen);
            var distance = Navigation.GetDistance(waypoint, position);
            if (distance < 5)
            {
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_E);
                Logger.LogInformation("已到达路径点附近");
                break;
            }
            // TODO 异常情况直接放到一个函数中处理，然后退出
            // 超时
            if (IsTimedOut(startTime))
            {
                Logger.LogWarning("执行超时，跳过路径点");
                SkipWaypoint = true;
                break;
            }
            // 距离终止判断
            if (distance > 500)
            {
                Logger.LogWarning("距离过远，跳过路径点");
                SkipWaypoint = true;
                break;
            }
            // 卡死
            // TODO 攀爬时应该跳过，但是如何处理看似是walk，实际是攀爬的
            if (IsStuck(prevPositions, position, lastPositionRecord))
            {
                lastPositionRecord = DateTime.UtcNow;
                // 脱离卡死
                await GetOutOfTheJam();
                SkipWaypoint = true;
                break;
            }
            Logger.LogInformation($"接近目标点中3，距离为{distance}");
            // 旋转视角
            targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);
            // 根据移动模式选择相应的行为
            if (waypoint.MoveMode == MoveModeEnum.Fly.Code)
            {
                await FlightForward();
                await Delay(1000, cts);
            }

            if (waypoint.MoveMode == MoveModeEnum.Jump.Code)
            {
                ClimbForward();
                await Delay(1000, cts);
            }

            if (waypoint.MoveMode == MoveModeEnum.Swim.Code)
            {
                SwimmingForward();
                await Delay(1000, cts);
            }


            if (waypoint.MoveMode == MoveModeEnum.Walk.Code)
            {

                if (distance >= 20)
                {
                    if (hasCharacter)
                    {
                        CharacterSkillForward();
                        await Delay(200, cts);
                    }
                    else
                    {
                        SprintForward();
                        await Delay(500, cts);
                    }
                }
                else
                {
                    // 结束e技能
                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_E);
                    NormalForward();
                    await Delay(600, cts);
                }
            }

            Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
            Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_SHIFT);
        }
    }

    private async Task MoveCloseTo(Waypoint waypoint)
    {
        var screen = CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogInformation("精确接近目标点，位置({x2},{y2})", $"{waypoint.X:F1}", $"{waypoint.Y:F1}");
        if (waypoint.MoveMode == MoveModeEnum.Fly.Code && waypoint.Action == ActionEnum.StopFlying.Code)
        {
            //下落攻击接近目的地
            Logger.LogInformation("动作：下落攻击");
            Simulation.SendInput.Mouse.LeftButtonClick();
            await Delay(1000, cts);
        }
        await _rotateTask.WaitUntilRotatedTo(targetOrientation, 2);
        var wPressed = false;
        var stepsTaken = 0;
        while (!cts.IsCancellationRequested)
        {
            stepsTaken++;
            if (stepsTaken > 8)
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
            _rotateTask.RotateToApproach(targetOrientation, screen); //不再改变视角
            if (waypoint.MoveMode == MoveModeEnum.Walk.Code)
            {
                // 小碎步接近
                Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W).Sleep(60).KeyUp(User32.VK.VK_W);
                await Delay(200, cts);
                continue;
            }
            if (!wPressed)
            {
                Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
            }
            await Delay(100, cts);
        }
        if (wPressed)
        {
            Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
        }
    }

    private async Task AfterMoveToTarget(Waypoint waypoint)
    {
        if (waypoint.Action == ActionEnum.NahidaCollect.Code
            || waypoint.Action == ActionEnum.PickAround.Code
            || waypoint.Action == ActionEnum.Fight.Code)
        {
            var handler = ActionFactory.GetHandler(waypoint.Action);
            await handler.RunAsync(cts);
        }
    }

    // 普通向前移动
    private void NormalForward()
    {
        Logger.LogInformation("正常向前移动");
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
    }

    // 攀爬向前移动
    private void ClimbForward()
    {
        Logger.LogInformation("进行攀爬");
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
        // TODO 角色处理逻辑：卡其娜，西诺宁
    }

    // 疾跑向前移动 
    private void SprintForward()
    {
        Logger.LogInformation("疾跑向前移动");
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_SHIFT);
    }

    // 飞行向前移动
    private async Task FlightForward()
    {
        Logger.LogInformation("进入飞行模式");
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);

        var screen = CaptureToRectArea();
        var isFlying = Bv.GetMotionStatus(screen) == MotionStatus.Fly;

        if (!isFlying)
        {
            Logger.LogInformation("未进入飞行状态，按下空格展开翅膀");
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
            await Delay(200, cts); // 延迟，确保飞行动作完成
        }
    }

    // 游泳向前移动
    private void SwimmingForward()
    {
        Logger.LogInformation("进入游泳模式");
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        // TODO 添加芙宁娜处理
        // 有芙宁娜时定时释放e技能
    }

    private void CharacterSkillForward()
    {
        Logger.LogInformation("使用角色技能向前移动");
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_E).Sleep(500).KeyUp(User32.VK.VK_E);
        // TODO 根据不同角色进行处理：夜兰，闲云，散兵，早柚，玛拉妮，基尼奇
        // 玛拉妮，散兵正常移动即可，但是夜兰，早柚是持续向前移动的，需要特殊处理
        // 闲云，基尼奇容易超出距离，但是问题不大
    }

    // 脱离卡死 
    private async Task GetOutOfTheJam()
    {
        Logger.LogWarning("脱离卡死状态");
        // 脱离攀爬状态
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
        await Delay(1500, cts);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
        await Delay(500, cts);
        // 向后移动
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_S);
        await Task.Delay(1500);
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_S);
        // 向左移动
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_A);
        await Task.Delay(1000);
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_A);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
        await Delay(500, cts);
        // 向右移动
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_D);
        await Task.Delay(1000);
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_D);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
        await Delay(500, cts);
        // 跳跃
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
        await Task.Delay(200); // 等待跳跃动作
    }


    public bool IsTimedOut(DateTime startTime)
    {
        var now = DateTime.UtcNow;
        return (now - startTime).TotalSeconds > 60;
    }

    public bool IsStuck(List<Point2f> prevPositions, Point2f position, DateTime lastPositionRecord)
    {
        if ((DateTime.UtcNow - lastPositionRecord).TotalMilliseconds > 1000)
        {
            prevPositions.Add(position);
            if (prevPositions.Count > 8)
            {
                var delta = prevPositions[^1] - prevPositions[^8];
                if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 3)
                {
                    Logger.LogWarning("疑似卡死，尝试脱离并跳过路径点");
                    return true;
                }
            }
        }
        return false;
    }
}
