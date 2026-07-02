using Fischless.WindowsInput;
using System;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Session;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator;

public class Simulation
{
    private static readonly InputSimulator NativeInput = new();

    public static IInputSimulator SendInput =>
        (IInputSimulator?)GameSessionContext.Current?.Input ?? NativeInput;

    public static MouseEventSimulator MouseEvent { get; } = new();

    public static PostMessageSimulator PostMessage(IntPtr hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }

    public static void ReleaseAllKey()
    {
        if (GameSessionContext.Current?.Input is { } sessionInput)
        {
            sessionInput.ReleaseAllAsync().GetAwaiter().GetResult();
            return;
        }

        foreach (User32.VK key in Enum.GetValues(typeof(User32.VK)))
        {
            // 检查键是否被按下
            if (IsKeyDown(key)) // 强制转换 VK 枚举为 int
            {
                TaskControl.Logger.LogDebug($"解除{key}的按下状态.");
                SendInput.Keyboard.KeyUp(key);
            }
        }
		SendInput.Mouse.LeftButtonUp();
        SendInput.Mouse.RightButtonUp();
        SendInput.Mouse.MiddleButtonUp();
    }

    public static bool IsKeyDown(User32.VK key)
    {
        if (GameSessionContext.Current?.Input is { } sessionInput)
        {
            return sessionInput.InputDeviceState.IsKeyDown(key);
        }

        // 获取按键状态
        var state = User32.GetAsyncKeyState((int)key);

        // 检查高位是否为 1（表示按键被按下）
        return (state & 0x8000) != 0;
    }
}
