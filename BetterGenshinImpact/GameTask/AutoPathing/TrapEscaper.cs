using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Simulator.Extensions;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class TrapEscaper(CancellationToken ct)
{
    private readonly CameraRotateTask _rotateTask = new(ct);
    private static readonly Random _random = new Random();
    private int _lastActionIndex = 0;
    public static DateTime LastActionTime = DateTime.UtcNow;
    private static int _randomAngle = 0;

    private void IncreaseRandomAngle()
    {
        _randomAngle += _random.Next(30, 45);
    }

    private void ReduceRandomAngle()
    {
        _randomAngle += _random.Next(-45, -30);
    }

    public async Task MoveTo(WaypointForTrack waypoint)
    {
        var startTime = DateTime.UtcNow;
        bool left = false;
        var screen = CaptureToRectArea();
        var position = Navigation.GetPosition(screen, waypoint.MapName, waypoint.MapMatchMethod);
        LastActionTime = DateTime.UtcNow;
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);

        // 按下w，一直走
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            if ((now - LastActionTime).TotalSeconds > 5)
            {
                break;
            }
            if ((now - startTime).TotalSeconds > 25)
            {
                Logger.LogError("卡死脱困超时！");
                break;
            }

            screen = CaptureToRectArea();
            position = Navigation.GetPosition(screen, waypoint.MapName, waypoint.MapMatchMethod);

            // 旋转视角
            /* 这里的角度增加了一个randomAngle角度，用来在原角度不适用的情况下修改角度以适应复杂环境
               randomAngle会定期归零，不会任何程度上影响地图追踪的结果（指到达既设点位）
               randomAngle为类变量，会在需要修改角度的情况下进行更改，更改时会附带有重置计时器_lastActionTime的代码
               总体的自动避障逻辑为：
               0. 检测是否卡在障碍物上，如果是则执行大脱困
               1. 检测前面是否有障碍物，如果是则执行小脱困
               2. 重复0和1，角度会一直增加，达到“转一圈”的360度脱困效果，若成功脱困则将randomAngle归零
               */
            targetOrientation = Navigation.GetTargetOrientation(waypoint, position) + _randomAngle;

            //执行旋转
            await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            //
            //这里是随机角度的归零逻辑，在脱困执行一秒后将randomAngle设为0以将实际角度重置为正面向点位的角度
            //其实就是在一段时间内进行角度的修改以实现自动避障
            if (_randomAngle != 0)
            {
                _randomAngle %= 360; //角度增加到360度时也会归零
                if ((DateTime.UtcNow - LastActionTime).TotalSeconds > 1.5)
                {
                    _randomAngle = 0;
                }
            }
            // 设置为非攀爬时误进入攀爬，自动脱离（小脱困）
            // 小脱困逻辑，在进入攀爬时，即后一帧会自动脱离，因此无需再执行脱困代码
            // 进入攀爬就代表前面有较高的物体（障碍物）阻挡，所以必须“旋转角度”以辅助绕过障碍物！！！

            // 先排除攀爬和飞行的情况
            if (waypoint.MoveMode != MoveModeEnum.Climb.Code &&
                waypoint.MoveMode != MoveModeEnum.Fly.Code)
                if (Bv.GetMotionStatus(screen) == MotionStatus.Climb)
                {
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    Simulation.SendInput.SimulateAction(GIActions.Drop);
                    Sleep(75);
                    Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                    Sleep(700);
                    Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);

                    LastActionTime = DateTime.UtcNow;

                    //！！！！！！！！这里修改了randomAngle的值，用于在脱困后随机旋转角度！！！！！！！！
                    if (!left)
                    {
                        IncreaseRandomAngle();
                    }
                    else
                    {
                        ReduceRandomAngle();
                    }

                    continue;
                }

            await Delay(100, ct);
        }

        // 抬起w键
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
    }

    public async Task RotateAndMove()
    {
        IncreaseRandomAngle();
        // 脱离攀爬状态
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        Simulation.SendInput.SimulateAction(GIActions.Drop);
        await Delay(75, ct);
        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
        await Delay(500, ct);

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
        Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
        Sleep(500);
        Simulation.SendInput.SimulateAction(GIActions.Jump);
        Sleep(delay);
        Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
    }

    private void MoveLeft(int delay)
    {
        Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
        Sleep(300);
        Simulation.SendInput.SimulateAction(GIActions.Jump);
        Sleep(delay);
        Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
        Simulation.SendInput.SimulateAction(GIActions.Drop);
    }

    private void MoveRight(int delay)
    {
        Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyDown);
        Sleep(300);
        Simulation.SendInput.SimulateAction(GIActions.Jump);
        Sleep(delay);
        Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyUp);
        Simulation.SendInput.SimulateAction(GIActions.Drop);
    }
}
