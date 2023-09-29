using System.Threading;
using System.Windows;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using static Windows.Win32.PInvoke;

namespace BetterGenshinImpact.Core.Recognition.Simulator;

public class MouseEventSimulator
{
    public static void Move(int x, int y)
    {
        mouse_event(
            MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE|MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE,
            x * 65535 / PrimaryScreen.DESKTOP.Width,
            y * 65535 / PrimaryScreen.DESKTOP.Height,
            0,
            0);
    }

    public static void LeftButtonDown()
    {
        mouse_event(MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
    }

    public static void LeftButtonUp()
    {
        mouse_event(MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
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
