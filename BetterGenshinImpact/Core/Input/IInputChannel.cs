using BetterGenshinImpact.Core.Simulator.Extensions;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 输入通道：后端内部的一路输出（前台或后台）
/// </summary>
public interface IInputChannel
{
    IKeyboardInput Keyboard { get; }

    IMouseInput Mouse { get; }

    /// <summary>
    /// 模拟游戏动作，按键由用户的键位配置决定
    /// </summary>
    IInputChannel SimulateAction(GIActions action, KeyType type = KeyType.KeyPress);

    /// <summary>
    /// 按键是否处于按下状态，含鼠标键 VK_LBUTTON 等
    /// </summary>
    bool IsKeyDown(User32.VK key);

    IInputChannel Sleep(int ms);

    /// <summary>
    /// 释放本通道中处于按下状态的按键
    /// </summary>
    void ReleaseAll();
}
