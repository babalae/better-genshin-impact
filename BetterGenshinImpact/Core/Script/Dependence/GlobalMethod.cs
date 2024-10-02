using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Helpers;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class GlobalMethod
{
    public static async Task Sleep(int millisecondsTimeout)
    {
        await Task.Delay(millisecondsTimeout, CancellationContext.Instance.Cts.Token);
    }

    #region 键盘操作

    public static void KeyDown(string key)
    {
        Simulation.SendInput.Keyboard.KeyDown(ToVk(key));
    }

    public static void KeyUp(string key)
    {
        Simulation.SendInput.Keyboard.KeyUp(ToVk(key));
    }

    public static void KeyPress(string key)
    {
        Simulation.SendInput.Keyboard.KeyPress(ToVk(key));
    }

    private static User32.VK ToVk(string key)
    {
        try
        {
            return User32Helper.ToVk(key);
        }
        catch
        {
            throw new ArgumentException($"键盘编码必须是VirtualKeyCodes枚举中的值，当前传入的 {key} 不合法");
        }
    }

    #endregion 键盘操作

    #region 鼠标操作

    public static void MoveMouseBy(int x, int y)
    {
        Simulation.SendInput.Mouse.MoveMouseBy(x, y);
    }

    public static void MoveMouseTo(int x, int y)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(x, y);
    }

    public static void LeftButtonClick()
    {
        Simulation.SendInput.Mouse.LeftButtonClick();
    }

    public static void RightButtonClick()
    {
        Simulation.SendInput.Mouse.RightButtonClick();
    }

    public static void MiddleButtonClick()
    {
        Simulation.SendInput.Mouse.MiddleButtonClick();
    }

    #endregion 鼠标操作
}
