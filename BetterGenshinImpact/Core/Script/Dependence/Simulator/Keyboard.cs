using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Helpers;
using System;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Script.Dependence.Simulator;

public class Keyboard
{
    public void KeyDown(string key)
    {
        Simulation.SendInput.Keyboard.KeyDown(ToVk(key));
    }

    public void KeyUp(string key)
    {
        Simulation.SendInput.Keyboard.KeyUp(ToVk(key));
    }

    public void KeyPress(string key)
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
}
