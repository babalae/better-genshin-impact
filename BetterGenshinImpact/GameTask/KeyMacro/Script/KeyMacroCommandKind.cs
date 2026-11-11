namespace BetterGenshinImpact.GameTask.KeyMacro.Script;

/// <summary>
/// 按键宏的步骤类型：一条指令或一行注释
/// </summary>
public enum KeyMacroCommandKind
{
    /// <summary>
    /// 整行注释
    /// </summary>
    Comment,

    /// <summary>
    /// 按下键盘键并保持：keydown(k)
    /// </summary>
    KeyDown,

    /// <summary>
    /// 松开键盘键：keyup(k)
    /// </summary>
    KeyUp,

    /// <summary>
    /// 按一下键盘键：keypress(k)
    /// </summary>
    KeyPress,

    /// <summary>
    /// 按下鼠标键并保持：mousedown(b)
    /// </summary>
    MouseDown,

    /// <summary>
    /// 松开鼠标键：mouseup(b)
    /// </summary>
    MouseUp,

    /// <summary>
    /// 点击鼠标键：click(b)，可带游戏画面坐标 click(b, x, y)
    /// </summary>
    Click,

    /// <summary>
    /// 鼠标相对移动：moveby(x, y)
    /// </summary>
    MoveBy,

    /// <summary>
    /// 鼠标绝对移动到游戏画面坐标：moveto(x, y)，坐标以 1920×1080 为准
    /// </summary>
    MoveTo,

    /// <summary>
    /// 滚轮：scroll(n)
    /// </summary>
    Scroll,

    /// <summary>
    /// 等待：wait(秒)
    /// </summary>
    Wait
}

/// <summary>
/// 按键宏支持的鼠标键
/// </summary>
public enum KeyMacroMouseButton
{
    /// <summary>
    /// 左键
    /// </summary>
    Left,

    /// <summary>
    /// 右键
    /// </summary>
    Right,

    /// <summary>
    /// 中键
    /// </summary>
    Middle,

    /// <summary>
    /// 侧键 1
    /// </summary>
    X1,

    /// <summary>
    /// 侧键 2
    /// </summary>
    X2
}
