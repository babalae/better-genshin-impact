using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 键盘输入。传入鼠标键 VK（VK_LBUTTON、VK_RBUTTON、VK_MBUTTON、VK_XBUTTON1、VK_XBUTTON2）时按对应鼠标键处理
/// </summary>
public interface IKeyboardInput
{
    IKeyboardInput KeyDown(User32.VK key);

    IKeyboardInput KeyUp(User32.VK key);

    IKeyboardInput KeyPress(User32.VK key);

    IKeyboardInput Sleep(int ms);
}
