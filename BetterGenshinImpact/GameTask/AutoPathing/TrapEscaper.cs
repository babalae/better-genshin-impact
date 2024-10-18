using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class TrapEscaper(CancellationTokenSource cts)
{
    private  readonly CameraRotateTask _rotateTask = new(cts);
    private static readonly Random _random = new Random();
    private int _lastActionIndex = 0;
    public static DateTime LastActionTime = DateTime.UtcNow;
    private static int _randomAngle = 0;

    private void IncreaseRandomAngle()
    {
        _randomAngle += _random.Next(30, 45);
    }

    private void ReduseRandomAngle()
    {
        _randomAngle += _random.Next(-45, -30);
    }
    
    public async Task MoveTo(WaypointForTrack waypoint)
    {
        bool left = false;
        var StartTime = DateTime.UtcNow;
        var screen = CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        LastActionTime = DateTime.UtcNow;
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);
        
        // 按下w，一直走
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        while (!cts.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            if ((now - LastActionTime).TotalSeconds > 5)
            {
                break;
            }
            screen = CaptureToRectArea();
            position = Navigation.GetPosition(screen);
            
            // 旋转视角
            /* 这里的角度增加了一个randomAngle角度，用来在原角度不适用的情况下修改角度以适应复杂环境
               randomAngle会定期归零，不会任何程度上影响路径追踪的结果（指到达既设点位）
               randomAngle为类变量，会在需要修改角度的情况下进行更改，更改时会附带有重置计时器_lastActionTime的代码
               总体的自动避障逻辑为：
               0. 检测是否卡在障碍物上，如果是则执行大脱困
               1. 检测前面是否有障碍物，如果是则执行小脱困
               2. 重复0和1，角度会一直增加，达到“转一圈”的360度脱困效果，若成功脱困则将randomAngle归零
               */
            targetOrientation = Navigation.GetTargetOrientation(waypoint, position) + _randomAngle;
            
            //执行旋转
            await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);
            Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
            //
            //这里是随机角度的归零逻辑，在脱困执行一秒后将randomAngle设为0以将实际角度重置为正面向点位的角度
            //其实就是在一段时间内进行角度的修改以实现自动避障
            if (_randomAngle != 0)
            {
                _randomAngle %= 360; //角度增加到360度时也会归零
                if ((DateTime.UtcNow - LastActionTime).TotalSeconds > 1.5)
                    _randomAngle = 0;
            }
            // 设置为非攀爬时误进入攀爬，自动脱离（小脱困）
            // 小脱困逻辑，在进入攀爬时，即后一帧会自动脱离，因此无需再执行脱困代码
            // 进入攀爬就代表前面有较高的物体（障碍物）阻挡，所以必须“旋转角度”以辅助绕过障碍物！！！
            
            // 先排除攀爬和飞行的情况
            if (waypoint.MoveMode != MoveModeEnum.Climb.Code &&
                waypoint.MoveMode != MoveModeEnum.Fly.Code)
                if (Bv.GetMotionStatus(screen) == MotionStatus.Climb)
                {
                    Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
                    await Task.Delay(75);
                    Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_S);
                    await Task.Delay(700);
                    Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_S);
                    
                    LastActionTime = DateTime.UtcNow;

                    //！！！！！！！！这里修改了randomAngle的值，用于在脱困后随机旋转角度！！！！！！！！
                    if (!left)
                    {
                        IncreaseRandomAngle();
                    }
                    else
                    {
                        ReduseRandomAngle();
                    }
                    continue;
                }

            await Delay(100, cts);
        }

        // 抬起w键
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
    }
    public async Task RotateAndMove()
    {
        IncreaseRandomAngle();
        // 脱离攀爬状态
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
        Delay(75, cts).Wait();
        Simulation.SendInput.Mouse.LeftButtonClick();
        Delay(500, cts).Wait();

        TimeSpan timeSinceLastAction = DateTime.UtcNow - LastActionTime;

        if (timeSinceLastAction.TotalSeconds >= 10)
        {
            _lastActionIndex = 0;
        }
        else
        {
            _lastActionIndex++;
        }
        var difference = _lastActionIndex * 1000;

        switch (_lastActionIndex % 3)
        {
            case 0:
                // 向后移动
                MoveBackward(1000 + difference);
                break;
            case 1:
                // 向左移动
                MoveLeft(700 + difference);
                break;
            case 2:
                // 向右移动
                MoveRight(700 + difference);
                break;
        }
        LastActionTime = DateTime.UtcNow;
    }

    private void MoveBackward(int delay)
    {
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_S);
        Task.Delay(500).Wait();
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
        Task.Delay(delay).Wait();
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_S);
    }

    private void MoveLeft(int delay)
    {
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_A);
        Task.Delay(300).Wait();
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
        Task.Delay(delay).Wait();
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_A);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
    }

    private void MoveRight(int delay)
    {
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_D);
        Task.Delay(300).Wait();
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
        Task.Delay(delay).Wait();
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_D);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
    }

    private async Task Delay(int milliseconds, CancellationTokenSource cts)
    {
        if (!cts.IsCancellationRequested)
        {
            await Task.Delay(milliseconds);
        }
    }
}