using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.KeyMacro.Script;

/// <summary>
/// 一条指令的说明，供解析、补全和语法速查共用
/// </summary>
/// <param name="Name">指令名（小写）</param>
/// <param name="Kind">指令类型</param>
/// <param name="Signature">参数写法，例如 keypress(键)</param>
/// <param name="Example">示例，点击速查时插入</param>
/// <param name="Description">一句说明</param>
public sealed record KeyMacroCommandInfo(string Name, KeyMacroCommandKind Kind, string Signature, string Example, string Description);

/// <summary>
/// 按键宏支持的全部指令。只有基础键鼠指令和 wait，不包含战斗策略的包装指令
/// </summary>
public static class KeyMacroCommandCatalog
{
    /// <summary>
    /// 全部指令，按速查展示顺序排列
    /// </summary>
    public static IReadOnlyList<KeyMacroCommandInfo> All { get; } =
    [
        new("keypress", KeyMacroCommandKind.KeyPress, "keypress(键)", "keypress(e)", "按一下键盘键"),
        new("keydown", KeyMacroCommandKind.KeyDown, "keydown(键)", "keydown(e)", "按下键盘键并保持"),
        new("keyup", KeyMacroCommandKind.KeyUp, "keyup(键)", "keyup(e)", "松开键盘键"),
        new("click", KeyMacroCommandKind.Click, "click(鼠标键[, x, y])", "click(left)",
            "点击鼠标键：left、right、middle、x1、x2；可加坐标，如 click(left, 960, 540) 先移到该位置再点击"),
        new("mousedown", KeyMacroCommandKind.MouseDown, "mousedown(鼠标键)", "mousedown(left)", "按下鼠标键并保持"),
        new("mouseup", KeyMacroCommandKind.MouseUp, "mouseup(鼠标键)", "mouseup(left)", "松开鼠标键"),
        new("moveby", KeyMacroCommandKind.MoveBy, "moveby(x, y)", "moveby(100, 0)", "鼠标相对移动"),
        new("moveto", KeyMacroCommandKind.MoveTo, "moveto(x, y)", "moveto(960, 540)", "鼠标绝对移动到游戏画面坐标，以 1920×1080 为准"),
        new("scroll", KeyMacroCommandKind.Scroll, "scroll(格数)", "scroll(1)", "滚轮，正数向上"),
        new("wait", KeyMacroCommandKind.Wait, "wait(秒)", "wait(0.1)", "等待，单位秒")
    ];

    /// <summary>
    /// 指令名 → 指令说明（不区分大小写）
    /// </summary>
    private static readonly Dictionary<string, KeyMacroCommandInfo> ByName =
        All.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 指令类型 → 指令说明
    /// </summary>
    private static readonly Dictionary<KeyMacroCommandKind, KeyMacroCommandInfo> ByKind = All.ToDictionary(c => c.Kind);

    /// <summary>
    /// 按指令名查找
    /// </summary>
    public static bool TryGet(string name, out KeyMacroCommandInfo info) => ByName.TryGetValue(name.Trim(), out info!);

    /// <summary>
    /// 指令类型对应的指令名；注释返回空字符串
    /// </summary>
    public static string GetName(KeyMacroCommandKind kind) => ByKind.TryGetValue(kind, out var info) ? info.Name : string.Empty;
}
