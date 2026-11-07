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
            // 检查键是否被按下
            if (IsKeyDown(key)) // 强制转换 VK 枚举为 int
            {
                TaskControl.Logger.LogDebug($"解除{key}的按下状态.");
                SendInput.Keyboard.KeyUp(key);
            }
        }

        // 鼠标键同样只在确实处于按下状态时才抬起。
        // 无条件发送抬起会产生一条没有对应按下的 WM_*BUTTONUP，DefWindowProc 会把它转成 WM_CONTEXTMENU，
        // 在游戏/程序未处于前台时表现为在光标处“右键一下”弹出菜单。
        ReleaseMouseButtonIfPressed(User32.VK.VK_LBUTTON, () => SendInput.Mouse.LeftButtonUp());
        ReleaseMouseButtonIfPressed(User32.VK.VK_RBUTTON, () => SendInput.Mouse.RightButtonUp());
        ReleaseMouseButtonIfPressed(User32.VK.VK_MBUTTON, () => SendInput.Mouse.MiddleButtonUp());
    }

    /// <summary>
    /// 鼠标键处于按下状态时才发送抬起，避免产生脱离按下事件的孤立抬起消息。
    /// </summary>
    /// <param name="mouseButton">待检查的鼠标键虚拟键码。</param>
    /// <param name="release">实际发送抬起动作的方法。</param>
    private static void ReleaseMouseButtonIfPressed(User32.VK mouseButton, Action release)
    {
        if (!IsKeyDown(mouseButton))
        {
            return;
        }

        TaskControl.Logger.LogDebug($"解除{mouseButton}的按下状态.");
        release();
    }

    public static bool IsKeyDown(User32.VK key)
    {
        // 获取按键状态
        var state = User32.GetAsyncKeyState((int)key);

        // 检查高位是否为 1（表示按键被按下）
        return (state & 0x8000) != 0;
    }
}