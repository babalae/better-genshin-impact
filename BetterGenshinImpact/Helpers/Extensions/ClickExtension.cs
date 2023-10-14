using BetterGenshinImpact.Core.Simulator;
using OpenCvSharp;

namespace BetterGenshinImpact.Helpers.Extensions;

public static class ClickExtension
{
    public static void Click(this Point point)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(point.X * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            point.Y * 65535 * 1d / PrimaryScreen.WorkingArea.Height).LeftButtonClick();
    }

    public static void ClickCenter(this Rect rect)
    {
        Simulation.SendInput.Mouse.MoveMouseTo((rect.X + rect.Width * 1d / 2) * 65535 / PrimaryScreen.WorkingArea.Width,
            (rect.Y + rect.Height * 1d / 2) * 65535 / PrimaryScreen.WorkingArea.Height).LeftButtonClick();
    }

    public static void Click(int x, int y)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(x * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            y * 65535 * 1d / PrimaryScreen.WorkingArea.Height).LeftButtonClick();
    }
}