using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Helpers;
using System;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Script.Dependence.Simulator;

/// <summary>
/// JS 脚本中的 new PostMessage()，走后台通道。
/// 每次调用时从 InputHub 取通道，后端切换后自动生效；网页版下与前台通道相同
/// </summary>
public class PostMessage
{
    private static IInputChannel Channel => InputHub.Background;

    public void KeyDown(string key)
    {
        Channel.Keyboard.KeyDown(ToVk(key));
    }

    public void KeyUp(string key)
    {
        Channel.Keyboard.KeyUp(ToVk(key));
    }

    public void KeyPress(string key)
    {
        Channel.Keyboard.KeyPress(ToVk(key));
    }

    public void Click()
    {
        Channel.Mouse.LeftButtonClick();
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
