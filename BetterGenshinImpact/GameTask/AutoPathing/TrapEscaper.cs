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

/// <summary>
/// 卡死脱困器 / Trap Escaper.
/// </summary>
public class TrapEscaper
{
    private const int MaxTimeoutSeconds = 25;
    private const int IdleTimeoutSeconds = 5;
    private const int MaxEscaperDelayMs = 3000;

    private readonly CameraRotateTask _rotateTask;
    private readonly Random _random;
    private readonly CancellationToken _ct;

    private int _lastActionIndex = 0;
    
    /// <summary>
    /// 获取最后一次活动的时间 / Gets the time of the last action.
    /// </summary>
    public DateTime LastActionTime { get; private set; } = DateTime.UtcNow;

    private int _randomAngle = 0;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    public TrapEscaper(CancellationToken ct)
    {
        _ct = ct;
        _rotateTask = new CameraRotateTask(ct);
        _random = new Random();
    }

    /// <summary>
    /// 增加随机角度 / Adds a random angle.
    /// </summary>
    /// <param name="min">最小值 / Minimum value.</param>
    /// <param name="max">最大值 / Maximum value.</param>
    private void AddRandomAngle(int min, int max)
    {
        _randomAngle += _random.Next(min, max);
    }

    /// <summary>
    /// 增加脱困角度 / Increases the escape angle.
    /// </summary>
    private void IncreaseRandomAngle() => AddRandomAngle(30, 45);

    /// <summary>
    /// 减少脱困角度 / Reduces the escape angle.
    /// </summary>
    private void ReduceRandomAngle() => AddRandomAngle(-45, -30);

    /// <summary>
    /// 尝试移动脱困 / Attempts to move and escape the trap.
    /// </summary>
    /// <param name="waypoint">目标路点 / Target waypoint.</param>
    /// <returns>异步任务 / Asynchronous task.</returns>
    public async Task MoveTo(WaypointForTrack waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);

        var startTime = DateTime.UtcNow;
        var screen = CaptureToRectArea();
        var position = Navigation.GetPosition(screen, waypoint.MapName, waypoint.MapMatchMethod);
        LastActionTime = DateTime.UtcNow;
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
        await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);

        try
        {
            // 按下w，一直走
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            
            while (!_ct.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                if ((now - LastActionTime).TotalSeconds > IdleTimeoutSeconds)
                {
                    break;
                }
                if ((now - startTime).TotalSeconds > MaxTimeoutSeconds)
                {
                    Logger.LogError("卡死脱困超时！");
                    break;
                }

                screen = CaptureToRectArea();
                position = Navigation.GetPosition(screen, waypoint.MapName, waypoint.MapMatchMethod);

                // 旋转视角
                targetOrientation = Navigation.GetTargetOrientation(waypoint, position) + _randomAngle;

                //执行旋转
                await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);
                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);

                if (_randomAngle != 0)
                {
                    _randomAngle %= 360; //角度增加到360度时也会归零
                    if ((DateTime.UtcNow - LastActionTime).TotalSeconds > 1.5)
                    {
                        _randomAngle = 0;
                    }
                }

                // 逻辑清理：先排除攀爬和飞行的情况
                if (waypoint.MoveMode != MoveModeEnum.Climb.Code && waypoint.MoveMode != MoveModeEnum.Fly.Code)
                {
                    if (Bv.GetMotionStatus(screen) == MotionStatus.Climb)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        Simulation.SendInput.SimulateAction(GIActions.Drop);
                        await Delay(75, _ct);
                        
                        try
                        {
                            Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                            await Delay(700, _ct);
                        }
                        finally
                        {
                            Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                        }

                        LastActionTime = DateTime.UtcNow;

                        // 每次脱困时随机左右切换角度脱困
                        if (_random.Next(2) == 0)
                        {
                            IncreaseRandomAngle();
                        }
                        else
                        {
                            ReduceRandomAngle();
                        }

                        continue;
                    }
                }

                await Delay(100, _ct);
            }
        }
        finally
        {
            // 确保必定抬起w键
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        }
    }

    /// <summary>
    /// 旋转并移动脱困 / Rotates and moves to escape.
    /// </summary>
    /// <returns>异步任务 / Asynchronous task.</returns>
    public async Task RotateAndMove()
    {
        IncreaseRandomAngle();
        // 脱离攀爬状态
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        Simulation.SendInput.SimulateAction(GIActions.Drop);
        await Delay(75, _ct);
        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
        await Delay(500, _ct);

        TimeSpan timeSinceLastAction = DateTime.UtcNow - LastActionTime;

        if (timeSinceLastAction.TotalSeconds >= 10)
        {
            _lastActionIndex = 0;
        }
        else
        {
            _lastActionIndex++;
        }

        var difference = Math.Min(_lastActionIndex * 1000, MaxEscaperDelayMs);

        switch (_lastActionIndex % 3)
        {
            case 0: // 向后移动
                await MoveBackward(1000 + difference);
                break;
            case 1: // 向左移动
                await MoveLeft(700 + difference);
                break;
            case 2: // 向右移动
                await MoveRight(700 + difference);
                break;
        }

        LastActionTime = DateTime.UtcNow;
    }

    /// <summary>
    /// 模拟闪避移动 / Simulates evasive movement.
    /// </summary>
    /// <param name="moveAction">移动动作 / Move action.</param>
    /// <param name="pressDelay">按下延迟 / Press delay.</param>
    /// <param name="moveDelay">移动延迟 / Move delay.</param>
    /// <param name="shouldDrop">释放下落 / Whether to simulate drop action.</param>
    /// <returns>异步任务 / Asynchronous task.</returns>
    private async Task SimulateEvasiveMove(GIActions moveAction, int pressDelay, int moveDelay, bool shouldDrop)
    {
        try
        {
            Simulation.SendInput.SimulateAction(moveAction, KeyType.KeyDown);
            await Delay(pressDelay, _ct);
            Simulation.SendInput.SimulateAction(GIActions.Jump);
            await Delay(moveDelay, _ct);
        }
        finally
        {
            Simulation.SendInput.SimulateAction(moveAction, KeyType.KeyUp);
            if (shouldDrop)
            {
                Simulation.SendInput.SimulateAction(GIActions.Drop);
            }
        }
    }

    private async Task MoveBackward(int delay) => await SimulateEvasiveMove(GIActions.MoveBackward, 500, delay, false);

    private async Task MoveLeft(int delay) => await SimulateEvasiveMove(GIActions.MoveLeft, 300, delay, true);

    private async Task MoveRight(int delay) => await SimulateEvasiveMove(GIActions.MoveRight, 300, delay, true);
}
