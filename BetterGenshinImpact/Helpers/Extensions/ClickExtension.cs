using BetterGenshinImpact.Core.Input;
using OpenCvSharp;

namespace BetterGenshinImpact.Helpers.Extensions;

public static class ClickExtension
{
    public static void Click(this Point point)
    {
        InputHub.Foreground.Mouse.MoveMouseTo(point.X * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            point.Y * 65535 * 1d / PrimaryScreen.WorkingArea.Height).LeftButtonDown().Sleep(50).LeftButtonUp();
    }

    // public static void ClickCenter(this Rect rect, bool isRand = false)
    // {
    //     Simulation.SendInputEx.Mouse.MoveMouseTo((rect.X + (isRand ? Rd.Next(rect.Width) : rect.Width * 1d / 2)) * 65535 / PrimaryScreen.WorkingArea.Width,
    //         (rect.Y + (isRand ? Rd.Next(rect.Height) : rect.Height * 1d / 2)) * 65535 / PrimaryScreen.WorkingArea.Height).LeftButtonDown().Sleep(50).LeftButtonUp();
    // }

    public static IMouseInput Click(double x, double y)
    {
        return InputHub.Foreground.Mouse.MoveMouseTo(x * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            y * 65535 * 1d / PrimaryScreen.WorkingArea.Height).LeftButtonDown().Sleep(50).LeftButtonUp();
    }

    public static IMouseInput Move(double x, double y)
    {
        return InputHub.Foreground.Mouse.MoveMouseTo(x * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            y * 65535 * 1d / PrimaryScreen.WorkingArea.Height);
    }

    public static IMouseInput Move(Point p)
    {
        return Move(p.X, p.Y);
    }
}
