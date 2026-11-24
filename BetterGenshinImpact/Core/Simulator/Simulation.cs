using Fischless.WindowsInput;
using System;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator;

public class Simulation
{
    public static InputSimulator SendInput { get; } = new();

    public static MouseEventSimulator MouseEvent { get; } = new();

    public static PostMessageSimulator PostMessage(IntPtr hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }

    public static void ReleaseAllKey()
    {
        foreach (User32.VK key in Enum.GetValues(typeof(User32.VK)))
        {
            // 鼠标键不能当键盘键抬起，统一在下面按鼠标事件处理
            if (IsMouseButtonVk(key))
            {
                continue;
            }

            if (IsKeyDown(key))
            {
                TaskControl.Logger.LogDebug($"解除{key}的按下状态.");
                SendInput.Keyboard.KeyUp(key);
            }
        }

        // 鼠标键只在确实按下时才抬起。SendInput 的鼠标事件会投递到光标下的窗口，
        // 无条件发送的右键抬起落到 BGI 界面上时，会触发 WPF 的右键菜单
        if (IsKeyDown(User32.VK.VK_LBUTTON))
        {
            SendInput.Mouse.LeftButtonUp();
        }

        if (IsKeyDown(User32.VK.VK_RBUTTON))
        {
            SendInput.Mouse.RightButtonUp();
        }

        if (IsKeyDown(User32.VK.VK_MBUTTON))
        {
            SendInput.Mouse.MiddleButtonUp();
        }

        if (IsKeyDown(User32.VK.VK_XBUTTON1))
        {
            SendInput.Mouse.XButtonUp(0x0001);
        }

        if (IsKeyDown(User32.VK.VK_XBUTTON2))
        {
            SendInput.Mouse.XButtonUp(0x0002);
        }
    }

    private static bool IsMouseButtonVk(User32.VK key) => key is User32.VK.VK_LBUTTON or User32.VK.VK_RBUTTON
        or User32.VK.VK_MBUTTON or User32.VK.VK_XBUTTON1 or User32.VK.VK_XBUTTON2;

    public static bool IsKeyDown(User32.VK key)
    {
        // 获取按键状态
        var state = User32.GetAsyncKeyState((int)key);

        // 检查高位是否为 1（表示按键被按下）
        return (state & 0x8000) != 0;
    }
}