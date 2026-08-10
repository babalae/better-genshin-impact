using System;
using System.Windows.Forms;

namespace BetterGenshinImpact.Core.Monitor;

/// <summary>
/// Raw Input 鼠标按钮事件参数。
/// </summary>
public sealed class RawMouseButtonEventArgs(MouseButtons button, bool isDown) : EventArgs
{
    public MouseButtons Button { get; } = button;

    /// <summary>true 为按下，false 为抬起。</summary>
    public bool IsDown { get; } = isDown;
}

/// <summary>
/// Raw Input 鼠标滚轮事件参数。
/// </summary>
public sealed class RawMouseWheelEventArgs(int delta) : EventArgs
{
    /// <summary>滚轮增量，正值向上，负值向下。</summary>
    public int Delta { get; } = delta;
}
