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
        DebugInputTrace.Record("MouseEvent", "Move", $"x={x};y={y}");
    }

    public void MoveAbsolute(int x, int y)
    {
        User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_ABSOLUTE | User32.MOUSEEVENTF.MOUSEEVENTF_MOVE,
            x, y, 0, 0);
        DebugInputTrace.Record("MouseEvent", "MoveAbsolute", $"x={x};y={y}");
    }

    public void LeftButtonDown()
    {
        User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        DebugInputTrace.Record("MouseEvent", "LeftDown");
    }

    public void LeftButtonUp()
    {
        User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        DebugInputTrace.Record("MouseEvent", "LeftUp");
    }

    public bool Click(int x, int y)
    {
        if (x == 0 && y == 0) return false;

        Move(x, y);
        LeftButtonDown();
        Thread.Sleep(20);
        LeftButtonUp();
        DebugInputTrace.Record("MouseEvent", "Click", $"x={x};y={y}");
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
        var ok = Click(point);
        DebugInputTrace.Record("MouseEvent", "DoubleClick", $"x={point.X};y={point.Y}");
        return ok;
    }
}
