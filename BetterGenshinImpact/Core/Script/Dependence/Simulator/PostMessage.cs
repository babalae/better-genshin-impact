using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Helpers;
using System;
using BetterGenshinImpact.GameTask;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Script.Dependence.Simulator;

public class PostMessage
{
    private readonly PostMessageSimulator _postMessageSimulator = TaskContext.Instance().PostMessageSimulator;

    public void KeyDown(string key)
    {
        _postMessageSimulator.KeyDownBackground(ToVk(key));
    }

    public void KeyUp(string key)
    {
        _postMessageSimulator.KeyUpBackground(ToVk(key));
    }

    public void KeyPress(string key)
    {
        _postMessageSimulator.KeyPressBackground(ToVk(key));
    }

    public void Click()
    {
        _postMessageSimulator.LeftButtonClick();
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
