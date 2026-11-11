using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.KeyMacro.Script;

/// <summary>
/// 解析得到的一个步骤：一条有效指令或一行注释。
/// 只有与 <see cref="Kind"/> 对应的参数有意义，其余参数保持默认值
/// </summary>
public sealed record KeyMacroStep
{
    /// <summary>
    /// 步骤类型
    /// </summary>
    public KeyMacroCommandKind Kind { get; init; }

    /// <summary>
    /// 键盘指令的按键
    /// </summary>
    public User32.VK Key { get; init; }

    /// <summary>
    /// 鼠标指令的鼠标键
    /// </summary>
    public KeyMacroMouseButton Button { get; init; }

    /// <summary>
    /// moveby 的横向位移；moveto 和带坐标的 click 的横坐标（1920×1080 游戏画面坐标）
    /// </summary>
    public int X { get; init; }

    /// <summary>
    /// moveby 的纵向位移；moveto 和带坐标的 click 的纵坐标（1920×1080 游戏画面坐标）
    /// </summary>
    public int Y { get; init; }

    /// <summary>
    /// click 是否带坐标。带坐标时先移到 (<see cref="X"/>, <see cref="Y"/>) 再点击
    /// </summary>
    public bool HasPosition { get; init; }

    /// <summary>
    /// scroll 的格数，正数向上
    /// </summary>
    public int Amount { get; init; }

    /// <summary>
    /// wait 的时长（毫秒）
    /// </summary>
    public int WaitMs { get; init; }

    /// <summary>
    /// 注释步骤的文字（不含 // 或 #）
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// 指令所在行的行尾注释，没有时为 null
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// 所在行号，从 1 开始
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// 在行内的起始列，从 0 开始
    /// </summary>
    public int Column { get; init; }

    /// <summary>
    /// 在行内占用的字符数
    /// </summary>
    public int Length { get; init; }

    /// <summary>
    /// 是否为指令（不是注释）
    /// </summary>
    public bool IsCommand => Kind != KeyMacroCommandKind.Comment;
}
