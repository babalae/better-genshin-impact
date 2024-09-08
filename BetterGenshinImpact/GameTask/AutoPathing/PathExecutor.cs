using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
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
using BetterGenshinImpact.Helpers;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathExecutor(CancellationTokenSource cts)
{
    private double _dpi = 1;

    public async Task Pathing(PathingTask task)
    {
        _dpi = DpiHelper.ScaleY;
        if (task.Positions.Count == 0)
        {
            Logger.LogWarning("没有路径点，寻路结束");
            return;
        }

        task.Positions.First().Type = WaypointType.Teleport.Code;

        // 初始化查看地图
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this,
            "UpdateCurrentPathing", new object(), task));

        // 大地图传送的时候使用游戏坐标，追踪的时候应该使用2048地图图像坐标，这里临时做转换，后续改进
        var waypoints = new List<Waypoint>();
        foreach (var waypoint in task.Positions)
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

        await Delay(100, cts);
        Navigation.WarmUp(); // 提前加载地图特征点

        foreach (var waypoint in waypoints)
        {
            if (waypoint.Type == WaypointType.Teleport.Code)
            {
                // Logger.LogInformation("正在传送到{x},{y}", waypoint.X, waypoint.Y);
                var (tpX, tpY) = await new TpTask(cts).Tp(waypoint.X, waypoint.Y);
                var (tprX, tprY) = MapCoordinate.GameToMain2048(tpX, tpY);
                EntireMap.Instance.SetPrevPosition((float)tprX, (float)tprY); // 通过上一个位置直接进行局部特征匹配
                continue;
            }

            // Path不用走得很近，Target需要接近，但都需要先移动到对应位置

            await MoveTo(waypoint);

            if (waypoint.Type == WaypointType.Target.Code || !string.IsNullOrEmpty(waypoint.Action))
            {
                await MoveCloseTo(waypoint);
            }
        }
    }

    private async Task MoveTo(Waypoint waypoint)
    {
        var screen = CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogInformation("粗略接近途经点，位置({x2},{y2})", waypoint.X, waypoint.Y);
        await WaitUntilRotatedTo(targetOrientation, 5);
        var startTime = DateTime.UtcNow;
        var lastPositionRecord = DateTime.UtcNow;
        var fastMode = false;
        var prevPositions = new List<Point2f>();
        // 按下w，一直走
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        while (!cts.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            if ((now - startTime).TotalSeconds > 60)
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
            if ((now - lastPositionRecord).TotalMilliseconds > 1000)
            {
                lastPositionRecord = now;
                prevPositions.Add(position);
                if (prevPositions.Count > 8)
                {
                    var delta = prevPositions[^1] - prevPositions[^8];
                    if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 3)
                    {
                        Logger.LogWarning("疑似卡死，尝试脱离并跳过路径点");
                        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
                        await Delay(500, cts);
                        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
                        await Delay(500, cts);
                        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_S);
                        await Delay(500, cts);
                        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_A);
                        await Delay(500, cts);
                        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_D);
                        await Delay(500, cts);
                        return;
                    }
                }
            }
            // 旋转视角
            targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            RotateTo(targetOrientation, screen);
            // 根据指定方式进行移动
            if (waypoint.MoveMode == MoveModeEnum.Fly.Code)
            {
                var isFlying = Bv.GetMotionStatus(screen) == MotionStatus.Fly;
                if (!isFlying)
                {
                    Debug.WriteLine("未进入飞行状态，按下空格");
                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                    await Delay(200, cts);
                }
                continue;
            }
            // if (isFlying)
            // {
            //     Simulation.SendInput.Mouse.LeftButtonClick();
            //     await Delay(1000, cts);
            //     continue;
            // }
            if (waypoint.MoveMode == MoveModeEnum.Jump.Code)
            {
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                await Delay(200, cts);
                continue;
            }
            // 跑步或者游泳
            if (distance > 20 != fastMode)// 距离大于20时可以使用疾跑/自由泳
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
            await Delay(100, cts);
        }
        // 抬起w键
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
    }

    private async Task MoveCloseTo(Waypoint waypoint)
    {
        var screen = CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogInformation("精确接近目标点，位置({x2},{y2})", waypoint.X, waypoint.Y);
        if (waypoint.MoveMode == MoveModeEnum.Fly.Code && waypoint.Action == ActionEnum.StopFlying.Code)
        {
            //下落攻击接近目的地
            Logger.LogInformation("动作：下落攻击");
            Simulation.SendInput.Mouse.LeftButtonClick();
            await Delay(1000, cts);
        }
        await WaitUntilRotatedTo(targetOrientation, 2);
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
            RotateTo(targetOrientation, screen); //不再改变视角
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
        // 不管咋样，抬起w键
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
    }

    private int RotateTo(int targetOrientation, ImageRegion imageRegion, double controlRatio = 1)
    {
        var cao = CameraOrientation.Compute(imageRegion.SrcGreyMat);
        var diff = (cao - targetOrientation + 180) % 360 - 180;
        diff += diff < -180 ? 360 : 0;
        if (diff == 0)
        {
            return diff;
        }
        // 平滑的旋转视角
        if (Math.Abs(diff) > 90)
        {
            controlRatio = 5;
        }
        else if (Math.Abs(diff) > 30)
        {
            controlRatio = 3;
        }
        else if (Math.Abs(diff) > 5)
        {
            controlRatio = 2;
        }
        Simulation.SendInput.Mouse.MoveMouseBy((int)Math.Round(-controlRatio * diff * _dpi), 0);
        return diff;
    }

    private async Task WaitUntilRotatedTo(int targetOrientation, int maxDiff)
    {
        int count = 0;
        while (!cts.IsCancellationRequested)
        {
            var screen = CaptureToRectArea();
            if (Math.Abs(RotateTo(targetOrientation, screen)) < maxDiff)
            {
                break;
            }
            if (count > 50)
            {
                break;
            }
            await Delay(50, cts);
            count++;
        }
    }
}
