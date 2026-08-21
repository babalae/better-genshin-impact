using BetterGenshinImpact.Helpers;
using System.Threading;
using System.Windows;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator;

public class MouseEventSimulator
{
    public void Move(int x, int y)
    {
        User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_ABSOLUTE | User32.MOUSEEVENTF.MOUSEEVENTF_MOVE,
            x * 65535 / PrimaryScreen.WorkingArea.Width, y * 65535 / PrimaryScreen.WorkingArea.Height,
            0, 0);
    }

    public void MoveAbsolute(int x, int y)
    {
        User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_ABSOLUTE | User32.MOUSEEVENTF.MOUSEEVENTF_MOVE,
            x, y, 0, 0);
    }

    public void LeftButtonDown()
    {
        User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
    }

    public void LeftButtonUp()
    {
        User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }

    public bool Click(int x, int y)
    {
        if (x == 0 && y == 0) return false;

        Move(x, y);
        LeftButtonDown();
        Thread.Sleep(20);
        LeftButtonUp();
        return true;
    }

    public bool Click(Point point)
    {
        return Click((int)point.X, (int)point.Y);
    }

    public bool DoubleClick(Point point)
    {
        Click(point);
        Thread.Sleep(200);
        return Click(point);
    }
}
