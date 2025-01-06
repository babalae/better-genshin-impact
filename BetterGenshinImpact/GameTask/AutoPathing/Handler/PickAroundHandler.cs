using System;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Helpers;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator.Extensions;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 采集任务到达点位后执行拾取操作
/// </summary>
public class PickAroundHandler() : IActionHandler
{
    private CancellationToken _ct;

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        this._ct = ct;
        Logger.LogInformation("执行 {Text}", "小范围内自动拾取");

        double speed = 1.1;
        int turns = 1;
        if (waypointForTrack is { ActionParams: not null })
        {
            turns = StringUtils.TryParseInt(waypointForTrack.ActionParams, 1);
        }

        // 无加成幼年为 1, 少年为 1.1, 成年为 1.2, 若有移速加成, 再乘以加成倍率.
        CircularMotionCalculator calculator = new CircularMotionCalculator(speed);
        double oldRadiusT = 0;
        double angle = 0;
        for (int i = 0; i < turns; i++)
        {
            (double edgeT, double radiusT, double endAngle) = calculator.GetCircleInfo(i);
            await MoveToNextStartPoint(oldRadiusT, radiusT, angle);
            await MoveCircle(edgeT, 6);
            oldRadiusT = radiusT;
            angle = endAngle;
        }
    }

    public async Task MoveCircle(double edgeT, int n)
    {
        Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
        await Delay(30, _ct);
        while (n-- > 0)
        {
            Simulation.SendInput.Mouse.MiddleButtonClick();
            await Delay((int)Math.Round(edgeT), _ct);
        }

        Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
        await Delay(200, _ct);
    }

    public async Task MoveAfterTurn(User32.VK vk, int ms = 0)
    {
        Simulation.SendInput.Keyboard.KeyPress(vk);
        await Delay(200, _ct);
        Simulation.SendInput.Mouse.MiddleButtonClick();
        await Delay(500, _ct);
        if (ms > 0)
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            await Delay(ms, _ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            await Delay(200, _ct);
        }
    }

    public async Task MoveToNextStartPoint(double oldRadius, double newRadius, double angle)
    {
        double x = newRadius - oldRadius * Math.Cos(angle);
        double y = oldRadius * Math.Sin(angle);
        Simulation.SendInput.Mouse.MiddleButtonClick();
        await Delay(500, _ct);
        await MoveAfterTurn(GIActions.MoveBackward.ToActionKey().ToVK(), (int)Math.Round(y) + 200);
        await MoveAfterTurn(GIActions.MoveLeft.ToActionKey().ToVK(), (int)Math.Round(x));
    }
}

public class CircularMotionCalculator
{
    private const int Start = 600;
    private const int Interval = 400;
    private const double CircleT = 33000.0;
    private const double RadiusT = CircleT / (2 * Math.PI);

    private double _speed;
    private double _viewResetT;
    private double _mixAngle;
    private double _mixX;
    private double _mixY;

    public CircularMotionCalculator(double speed = 1.1)
    {
        Speed = speed;
    }

    public double Speed
    {
        get => _speed;
        set
        {
            _speed = value;
            UpdateSpeed(value);
        }
    }

    private void UpdateSpeed(double speed)
    {
        _viewResetT = 350 * speed;
        _mixAngle = (_viewResetT / CircleT + 1.0 / 4) * 2 * Math.PI;
        (_mixX, _mixY) = GetArcPoint(_viewResetT / _mixAngle, _mixAngle);
    }

    private (double, double) GetArcPoint(double radius, double angle)
    {
        return (radius * (1 - Math.Cos(angle)), radius * Math.Sin(angle));
    }

    public (double, double, double) GetCircleInfo(int index)
    {
        double edgeT = Start + index * Interval;
        double angle = (edgeT / CircleT + 1.0 / 4) * Math.PI;
        var (restX, restY) = GetArcPoint(RadiusT, 2 * angle - _mixAngle);
        double x = _mixX - restX;
        double y = _mixY + restY;
        double smallRadiusT = Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2)) / (2 * Math.Sin(angle));
        double endAngle = angle - _mixAngle + Math.Atan2(x, y) + Math.PI / 2;
        return (edgeT / Speed, smallRadiusT / Speed, endAngle);
    }
}