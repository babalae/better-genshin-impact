using BetterGenshinImpact.Helpers;
using System.Threading;
using System.Windows;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator;

public class MouseEventSimulator
{
    public static void Move(int x, int y)
    {
        User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_ABSOLUTE | User32.MOUSEEVENTF.MOUSEEVENTF_MOVE,
            x * 65535 / PrimaryScreen.DESKTOP.Width, y * 65535 / PrimaryScreen.DESKTOP.Height,
            0, 0);
    }

    public static void LeftButtonDown()
    {
        User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
    }

    public static void LeftButtonUp()
    {
        User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }

    public static bool Click(int x, int y)
    {
        if (x == 0 && y == 0)
        {
            return false;
        }
        Move(x, y);
        LeftButtonDown();
        Thread.Sleep(20);
        LeftButtonUp();
        return true;
    }

    public static bool Click(Point point)
    {
        return Click((int)point.X, (int)point.Y);
    }

    public static bool DoubleClick(Point point)
    {
        Click(point);
        Thread.Sleep(200);
        return Click(point);
    }
}
