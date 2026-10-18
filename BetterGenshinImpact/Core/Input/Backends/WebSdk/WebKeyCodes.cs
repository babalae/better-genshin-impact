using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Input.Backends.WebSdk;

/// <summary>
/// VK 到 KeyboardEvent.code 的映射，SDK 的 keyDown / keyUp / tapKey 接收 code 字符串。
/// 泛指的 Shift / Ctrl / Alt 映射为左侧键
/// </summary>
public static class WebKeyCodes
{
    public static bool TryGetCode(User32.VK vk, out string code)
    {
        var value = (int)vk;
        code = value switch
        {
            >= 0x41 and <= 0x5A => $"Key{(char)value}",
            >= 0x30 and <= 0x39 => $"Digit{(char)value}",
            >= 0x70 and <= 0x7B => $"F{value - 0x6F}",
            >= 0x60 and <= 0x69 => $"Numpad{value - 0x60}",
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 or 0xA0 => "ShiftLeft",
            0xA1 => "ShiftRight",
            0x11 or 0xA2 => "ControlLeft",
            0xA3 => "ControlRight",
            0x12 or 0xA4 => "AltLeft",
            0xA5 => "AltRight",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "ArrowLeft",
            0x26 => "ArrowUp",
            0x27 => "ArrowRight",
            0x28 => "ArrowDown",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x5B => "MetaLeft",
            0x5C => "MetaRight",
            0x6A => "NumpadMultiply",
            0x6B => "NumpadAdd",
            0x6D => "NumpadSubtract",
            0x6E => "NumpadDecimal",
            0x6F => "NumpadDivide",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0xBA => "Semicolon",
            0xBB => "Equal",
            0xBC => "Comma",
            0xBD => "Minus",
            0xBE => "Period",
            0xBF => "Slash",
            0xC0 => "Backquote",
            0xDB => "BracketLeft",
            0xDC => "Backslash",
            0xDD => "BracketRight",
            0xDE => "Quote",
            _ => string.Empty,
        };
        return code.Length > 0;
    }
}
