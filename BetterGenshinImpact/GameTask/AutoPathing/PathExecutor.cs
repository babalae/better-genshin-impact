using BetterGenshinImpact.Core.Simulator;
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

public class PathExecutor(CancellationTokenSource cts)
{
    private readonly CameraRotateTask _rotateTask = new(cts);
    
    private static DateTime _lastActionTime = DateTime.MinValue;
    private static int _lastActionIndex = 0;
    
    private static Random _ran = new Random();
    private static int _randomAngle = 0;

    public async Task Pathing(PathingTask task)
    {
        if (!task.Positions.Any())
        {
            Logger.LogWarning("没有路径点，寻路结束");
            return;
        }

        InitializePathing(task);

        var waypoints = ConvertWaypointsForTrack(task.Positions);

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
            Simulation.SendInput.Mouse.RightButtonUp();
        }
    }

    private void InitializePathing(PathingTask task)
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this,
            "UpdateCurrentPathing", new object(), task));
    }

    private List<WaypointForTrack> ConvertWaypointsForTrack(List<Waypoint> positions)
    {
        // 把 X Y 转换为 MatX MatY
        return positions.Select(waypoint => new WaypointForTrack(waypoint)).ToList();
    }

    private async Task HandleTeleportWaypoint(WaypointForTrack waypoint)
    {
        var forceTp = waypoint.Action == ActionEnum.ForceTp.Code;
        var (tpX, tpY) = await new TpTask(cts).Tp(waypoint.GameX, waypoint.GameY, forceTp);
        var (tprX, tprY) = MapCoordinate.GameToMain2048(tpX, tpY);
        EntireMap.Instance.SetPrevPosition((float)tprX, (float)tprY); // 通过上一个位置直接进行局部特征匹配
    }

    private async Task MoveTo(WaypointForTrack waypoint)
    {
        var screen = CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        Logger.LogInformation("粗略接近途经点，位置({x2},{y2})", $"{waypoint.GameX:F1}", $"{waypoint.GameY:F1}");
        await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);
        var startTime = DateTime.UtcNow;
        var lastPositionRecord = DateTime.UtcNow;
        var fastMode = false;
        var prevPositions = new List<Point2f>(); 
        var fastmodeColdTime = DateTime.UtcNow;
        
        // 按下w，一直走
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        while (!cts.IsCancellationRequested)
        {
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

            // 非攀爬状态下，检测是否卡死（大脱困触发器）
            if (waypoint.MoveMode != MoveModeEnum.Climb.Code)
            {   
                // &&后面的条件是用于执行小脱困时屏蔽大脱困，防止二者同时执行或交替执行导致角度变换过大
                if ((now - lastPositionRecord).TotalMilliseconds > 1000 && (DateTime.Now - _lastActionTime).TotalSeconds > 2)
                {
                    lastPositionRecord = now;
                    prevPositions.Add(position);
                    if (prevPositions.Count > 8)
                    {
                        var delta = prevPositions[^1] - prevPositions[^8];
                        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 3)
                        {
                            Logger.LogWarning("疑似卡死，尝试脱离");
                            
                            //调用大脱困代码
                            await EscapeTrap();
                            Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
                            continue;
                        }
                    }
                }
            }

            // 旋转视角
            /* 这里的角度增加了一个randomAngle角度，用来在原角度不适用的情况下修改角度以适应复杂环境
               randomAngle会定期归零，不会任何程度上影响路径追踪的结果（指到达既设点位）
               randomAngle为类变量，会在需要修改角度的情况下进行更改，更改时会附带有重置计时器_lastActionTime的代码
               更改角度的代码会
               总体的自动避障逻辑为：
               0. 检测是否卡在障碍物上，如果是则执行大脱困
               1. 检测前面是否有障碍物，如果是则执行小脱困
               2. 重复0和1，角度会一直增加，达到“转一圈”的360度脱困效果，若成功脱困则将randomAngle归零
               */
            targetOrientation = Navigation.GetTargetOrientation(waypoint, position) + _randomAngle;
            
            //执行旋转
            _rotateTask.RotateToApproach(targetOrientation, screen);
            
            //
            //这里是随机角度的归零逻辑，在脱困执行一秒后将randomAngle设为0以将实际角度重置为正面向点位的角度
            //其实就是在一段时间内进行角度的修改以实现自动避障
            if (_randomAngle != 0)
            {
                _randomAngle %= 360; //角度增加到360度时也会归零
                if ((DateTime.Now - _lastActionTime).TotalSeconds > 1)
                    _randomAngle = 0;
            }
            //
            
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
            if (waypoint.MoveMode == MoveModeEnum.Climb.Code)
            {
                if (Bv.GetMotionStatus(screen) != MotionStatus.Climb)
                {
                    Debug.WriteLine("未进入攀爬状态，按下空格");
                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                    await Delay(200, cts);
                }
                continue;
            }
            if (waypoint.MoveMode == MoveModeEnum.Jump.Code)
            {
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                await Delay(200, cts);
                continue;
            }
            // 设置为非攀爬时误进入攀爬，自动脱离（小脱困）
            // 小脱困逻辑，在进入攀爬时，即后一帧会自动脱离，因此无需再执行脱困代码
            // 进入攀爬就代表前面有较高的物体（障碍物）阻挡，所以必须“旋转角度”以辅助绕过障碍物！！！
            
            // 先排除攀爬和飞行的情况
            if (waypoint.MoveMode != MoveModeEnum.Climb.Code &&
                waypoint.MoveMode != MoveModeEnum.Fly.Code)
                if (Bv.GetMotionStatus(screen) == MotionStatus.Climb)
                {
                    Debug.WriteLine("进入攀爬状态，按下x");
                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
                    
                    //重置计时器
                    _lastActionTime = DateTime.Now;
                    
                    //！！！！！！！！这里修改了randomAngle的值，用于在脱困后随机旋转角度！！！！！！！！
                    _randomAngle += _ran.Next(15, 30);
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
            else if (waypoint.MoveMode != MoveModeEnum.Climb.Code)//否则自动短疾跑
            {
                if (distance > 10)
                {
                    if (Math.Abs((fastmodeColdTime-now).TotalMilliseconds) > 2500) //冷却时间2.5s，回复体力用
                    {
                        fastmodeColdTime = now;
                        Simulation.SendInput.Mouse.RightButtonClick();
                    }
                }
            }

            await Delay(100, cts);
        }

        // 抬起w键
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
    }

    private bool IsStuck(DateTime now, DateTime lastPositionRecord, List<Point2f> prevPositions, Point2f position)
    {
        if ((now - lastPositionRecord).TotalMilliseconds > 1000)
        {
            prevPositions.Add(position);
            if (prevPositions.Count > 8)
            {
                var delta = prevPositions[^1] - prevPositions[^8];
                if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 3)
                {
                    return true;
                }
            }
        }

        return false;
    }
    
    // 大脱困逻辑，
    private async Task EscapeTrap()
    {
        //！！！！！！！！这里修改了randomAngle的值，用于在脱困后随机旋转角度！！！！！！！！
        _randomAngle += _ran.Next(15, 30);
        // 脱离攀爬状态
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
        await Delay(1500, cts);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
        await Delay(75, cts);
        Simulation.SendInput.Mouse.LeftButtonClick();
        await Delay(500, cts);
        
        TimeSpan timeSinceLastAction = DateTime.Now - _lastActionTime;
        
        if (timeSinceLastAction.TotalSeconds >= 5) //调用间隔大于5秒识别为成功脱困后的下一次脱困
        {
            // 从零开始
            _lastActionIndex = 0;
        }
        else
        {
            // 使用下一个动作
            _lastActionIndex++;
        }
        var difference = _lastActionIndex * 1000;

        switch (_lastActionIndex%3)
        {   
            case 0:
                // 向后移动
                Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_S);
                await Task.Delay(500);
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                await Task.Delay(1000+difference);
                Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_S);
                break;
                
            case 1:
                // 向左移动
                Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_A);
                await Task.Delay(300);
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                await Task.Delay(700+difference);
                Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_A);
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
                await Delay(500, cts);
                break;
                
            case 2:
                // 向右移动
                Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_D);
                await Task.Delay(300);
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                await Task.Delay(700+difference);
                Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_D);
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
                await Delay(500, cts);
                break;
        }
        //重置计时器
        _lastActionTime = DateTime.Now;
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
            await Delay(1000, cts);
        }

        await _rotateTask.WaitUntilRotatedTo(targetOrientation, 2);
        var stepsTaken = 0;
        while (!cts.IsCancellationRequested)
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

            await _rotateTask.WaitUntilRotatedTo(targetOrientation, 2);
            // 小碎步接近
            Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W).Sleep(60).KeyUp(User32.VK.VK_W);
            await Delay(50, cts);
        }

        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);

        // 到达目的地后停顿一秒
        await Delay(1000, cts);
    }

    private async Task AfterMoveToTarget(Waypoint waypoint)
    {
        if (waypoint.Action == ActionEnum.NahidaCollect.Code
            || waypoint.Action == ActionEnum.PickAround.Code
            || waypoint.Action == ActionEnum.Fight.Code)
        {
            var handler = ActionFactory.GetHandler(waypoint.Action);
            await handler.RunAsync(cts);
            await Delay(800, cts);
        }
    }
}
