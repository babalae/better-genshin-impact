using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.KeyMacro.Script;

/// <summary>
/// 解析选项。部分检查依赖宏的触发方式和快捷键
/// </summary>
/// <param name="Mode">触发方式，用于检查循环模式下是否缺少等待</param>
/// <param name="HotkeyKey">快捷键对应的键盘键；脚本里出现它时报错，避免宏触发自己</param>
/// <param name="HotkeyButton">快捷键对应的鼠标侧键；脚本里出现它时报错</param>
public sealed record KeyMacroParseOptions(
    KeyMacroTriggerMode Mode = KeyMacroTriggerMode.Once,
    User32.VK? HotkeyKey = null,
    KeyMacroMouseButton? HotkeyButton = null)
{
    /// <summary>
    /// 不做快捷键相关检查的默认选项
    /// </summary>
    public static KeyMacroParseOptions Default { get; } = new();
}
