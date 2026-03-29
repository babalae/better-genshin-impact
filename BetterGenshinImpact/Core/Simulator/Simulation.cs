using BetterGenshinImpact.Core.Simulator.Hardware;
using BetterGenshinImpact.GameTask.Common;
using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;
using System;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator;

public class Simulation
{
    public static InputSimulator SendInput { get; }

    public static MouseEventSimulator MouseEvent { get; } = new();

    static Simulation()
    {
        var keyboard = new RoutingKeyboardSimulator();
        var mouse = new RoutingMouseSimulator();
        SendInput = new InputSimulator(keyboard, mouse, new WindowsInputDeviceStateAdaptor());
        keyboard.Initialize(SendInput);
        mouse.Initialize(SendInput);
    }

    public static PostMessageSimulator PostMessage(IntPtr hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }

    public static void ReleaseAllKey()
    {
        foreach (User32.VK key in Enum.GetValues(typeof(User32.VK)))
        {
            // 检查键是否被按下
            if (IsKeyDown(key))
            {
                TaskControl.Logger.LogDebug($"解除{key}的按下状态.");
                SendInput.Keyboard.KeyUp(key);
            }
        }
    }

    public static bool IsKeyDown(User32.VK key)
    {
        var state = User32.GetAsyncKeyState((int)key);
        return (state & 0x8000) != 0;
    }
}
