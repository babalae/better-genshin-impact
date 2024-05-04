using BetterGenshinImpact.Core.Simulator;
using OpenCvSharp;
using System;
using Fischless.WindowsInput;

namespace BetterGenshinImpact.Helpers.Extensions;

public static class ClickExtension
{
    public static Random Rd = new Random();

    public static void Click(this Point point)
    {
        Simulation.SendInputEx.Mouse.MoveMouseTo(point.X * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            point.Y * 65535 * 1d / PrimaryScreen.WorkingArea.Height).LeftButtonDown().Sleep(50).LeftButtonUp();
    }

    // public static void ClickCenter(this Rect rect, bool isRand = false)
    // {
    //     Simulation.SendInputEx.Mouse.MoveMouseTo((rect.X + (isRand ? Rd.Next(rect.Width) : rect.Width * 1d / 2)) * 65535 / PrimaryScreen.WorkingArea.Width,
    //         (rect.Y + (isRand ? Rd.Next(rect.Height) : rect.Height * 1d / 2)) * 65535 / PrimaryScreen.WorkingArea.Height).LeftButtonDown().Sleep(50).LeftButtonUp();
    // }

    public static IMouseSimulator Click(double x, double y)
    {
        return Simulation.SendInputEx.Mouse.MoveMouseTo(x * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            y * 65535 * 1d / PrimaryScreen.WorkingArea.Height).LeftButtonDown().Sleep(50).LeftButtonUp();
    }

    public static IMouseSimulator Move(double x, double y)
    {
        return Simulation.SendInputEx.Mouse.MoveMouseTo(x * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            y * 65535 * 1d / PrimaryScreen.WorkingArea.Height);
    }

    public static IMouseSimulator Move(Point p)
    {
        return Move(p.X, p.Y);
    }
}
