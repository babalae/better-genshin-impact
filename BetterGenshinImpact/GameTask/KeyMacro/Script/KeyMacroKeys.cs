using System;
using System.Collections.Generic;
using System.Linq;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.KeyMacro.Script;

/// <summary>
/// 按键宏的按键名、鼠标键名与虚拟键的对照，以及界面上显示的易读名称。
/// 名称都不区分大小写；写回脚本时使用 <see cref="GetName"/> 返回的规范名称
/// </summary>
public static class KeyMacroKeys
{
    /// <summary>
    /// 按键名 → 虚拟键（不区分大小写）。同一个虚拟键的第一个名称是规范名称
    /// </summary>
    private static readonly Dictionary<string, User32.VK> KeyAliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 虚拟键 → 规范名称
    /// </summary>
    private static readonly Dictionary<User32.VK, string> CanonicalNames = [];

    /// <summary>
    /// 虚拟键 → 易读名称
    /// </summary>
    private static readonly Dictionary<User32.VK, string> DisplayNames = [];

    /// <summary>
    /// 鼠标键的虚拟键名，键盘指令里出现时提示改用鼠标指令
    /// </summary>
    private static readonly HashSet<string> MouseKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "lbutton", "rbutton", "mbutton", "xbutton1", "xbutton2",
        "vk_lbutton", "vk_rbutton", "vk_mbutton", "vk_xbutton1", "vk_xbutton2"
    };

    /// <summary>
    /// 鼠标键名 → 鼠标键（不区分大小写）
    /// </summary>
    private static readonly Dictionary<string, KeyMacroMouseButton> ButtonAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = KeyMacroMouseButton.Left,
        ["right"] = KeyMacroMouseButton.Right,
        ["middle"] = KeyMacroMouseButton.Middle,
        ["x1"] = KeyMacroMouseButton.X1,
        ["xbutton1"] = KeyMacroMouseButton.X1,
        ["x2"] = KeyMacroMouseButton.X2,
        ["xbutton2"] = KeyMacroMouseButton.X2
    };

    /// <summary>
    /// 补全与速查使用的按键名（规范名称），按常用程度排列
    /// </summary>
    public static IReadOnlyList<string> CompletionKeyNames { get; }

    /// <summary>
    /// 补全与速查使用的鼠标键名
    /// </summary>
    public static IReadOnlyList<string> CompletionButtonNames { get; } = ["left", "right", "middle", "x1", "x2"];

    static KeyMacroKeys()
    {
        // 字母、数字、功能键、小键盘按虚拟键编号连续排列
        for (var i = 0; i < 26; i++)
        {
            var name = ((char)('a' + i)).ToString();
            Register((User32.VK)(0x41 + i), name.ToUpperInvariant(), name);
        }

        for (var i = 0; i < 10; i++)
        {
            Register((User32.VK)(0x30 + i), i.ToString(), i.ToString());
        }

        for (var i = 0; i < 24; i++)
        {
            Register((User32.VK)(0x70 + i), $"F{i + 1}", $"f{i + 1}");
        }

        for (var i = 0; i < 10; i++)
        {
            Register((User32.VK)(0x60 + i), $"小键盘 {i}", $"numpad{i}");
        }

        // 常用功能键：第一个名称为规范名称，后面是兼容写法
        Register(User32.VK.VK_SPACE, "Space", "space");
        Register(User32.VK.VK_RETURN, "Enter", "enter", "return");
        Register(User32.VK.VK_TAB, "Tab", "tab");
        Register(User32.VK.VK_ESCAPE, "Esc", "esc", "escape");
        Register(User32.VK.VK_BACK, "Backspace", "backspace", "back");
        Register(User32.VK.VK_DELETE, "Delete", "delete", "del");
        Register(User32.VK.VK_INSERT, "Insert", "insert", "ins");
        Register(User32.VK.VK_HOME, "Home", "home");
        Register(User32.VK.VK_END, "End", "end");
        Register(User32.VK.VK_PRIOR, "PageUp", "pageup", "pgup", "prior");
        Register(User32.VK.VK_NEXT, "PageDown", "pagedown", "pgdn", "next");
        Register(User32.VK.VK_UP, "↑", "up");
        Register(User32.VK.VK_DOWN, "↓", "down");
        Register(User32.VK.VK_LEFT, "←", "left");
        Register(User32.VK.VK_RIGHT, "→", "right");
        Register(User32.VK.VK_SHIFT, "Shift", "shift");
        Register(User32.VK.VK_LSHIFT, "左 Shift", "lshift");
        Register(User32.VK.VK_RSHIFT, "右 Shift", "rshift");
        Register(User32.VK.VK_CONTROL, "Ctrl", "ctrl", "control");
        Register(User32.VK.VK_LCONTROL, "左 Ctrl", "lctrl", "lcontrol");
        Register(User32.VK.VK_RCONTROL, "右 Ctrl", "rctrl", "rcontrol");
        Register(User32.VK.VK_MENU, "Alt", "alt", "menu");
        Register(User32.VK.VK_LMENU, "左 Alt", "lalt", "lmenu");
        Register(User32.VK.VK_RMENU, "右 Alt", "ralt", "rmenu");
        Register(User32.VK.VK_CAPITAL, "CapsLock", "capslock", "capital");
        Register(User32.VK.VK_LWIN, "左 Win", "lwin");
        Register(User32.VK.VK_RWIN, "右 Win", "rwin");
        Register(User32.VK.VK_APPS, "菜单键", "apps");
        Register(User32.VK.VK_PAUSE, "Pause", "pause");
        Register(User32.VK.VK_SNAPSHOT, "PrintScreen", "printscreen", "snapshot");
        Register(User32.VK.VK_NUMLOCK, "NumLock", "numlock");
        Register(User32.VK.VK_SCROLL, "ScrollLock", "scrolllock");
        Register(User32.VK.VK_MULTIPLY, "小键盘 *", "multiply");
        Register(User32.VK.VK_ADD, "小键盘 +", "add");
        Register(User32.VK.VK_SUBTRACT, "小键盘 -", "subtract");
        Register(User32.VK.VK_DECIMAL, "小键盘 .", "decimal");
        Register(User32.VK.VK_DIVIDE, "小键盘 /", "divide");

        // 符号键只登记易读名称，脚本里使用 oem_* 虚拟键名（通过 TryParseKey 的虚拟键名兜底解析）
        DisplayNames[User32.VK.VK_OEM_1] = ";";
        DisplayNames[User32.VK.VK_OEM_PLUS] = "=";
        DisplayNames[User32.VK.VK_OEM_COMMA] = ",";
        DisplayNames[User32.VK.VK_OEM_MINUS] = "-";
        DisplayNames[User32.VK.VK_OEM_PERIOD] = ".";
        DisplayNames[User32.VK.VK_OEM_2] = "/";
        DisplayNames[User32.VK.VK_OEM_3] = "`";
        DisplayNames[User32.VK.VK_OEM_4] = "[";
        DisplayNames[User32.VK.VK_OEM_5] = "\\";
        DisplayNames[User32.VK.VK_OEM_6] = "]";
        DisplayNames[User32.VK.VK_OEM_7] = "'";

        // 补全列表：常用功能键在前，其后是字母、数字和功能键
        string[] common =
        [
            "space", "lshift", "lctrl", "lalt", "tab", "esc", "enter", "backspace",
            "up", "down", "left", "right", "shift", "ctrl", "alt", "capslock"
        ];
        CompletionKeyNames = common
            .Concat(Enumerable.Range(0, 26).Select(i => ((char)('a' + i)).ToString()))
            .Concat(Enumerable.Range(0, 10).Select(i => i.ToString()))
            .Concat(Enumerable.Range(1, 12).Select(i => $"f{i}"))
            .Concat(["delete", "insert", "home", "end", "pageup", "pagedown"])
            .Concat(Enumerable.Range(0, 10).Select(i => $"numpad{i}"))
            .ToList();
    }

    /// <summary>
    /// 登记一个按键：易读名称与名称列表，第一个名称为规范名称
    /// </summary>
    private static void Register(User32.VK vk, string displayName, params string[] names)
    {
        DisplayNames[vk] = displayName;
        CanonicalNames.TryAdd(vk, names[0]);
        foreach (var name in names)
        {
            KeyAliases.TryAdd(name, vk);
        }
    }

    /// <summary>
    /// 是否为鼠标键的虚拟键名（lbutton、xbutton1 等）
    /// </summary>
    public static bool IsMouseKeyName(string name) => MouseKeyNames.Contains(name.Trim());

    /// <summary>
    /// 解析键盘按键名。先查常用名称，再按 Windows 虚拟键名（可带或不带 vk_ 前缀）解析；鼠标键不算键盘按键
    /// </summary>
    /// <param name="name">按键名</param>
    /// <param name="vk">解析出的虚拟键</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParseKey(string name, out User32.VK vk)
    {
        vk = default;
        name = name.Trim();
        if (name.Length == 0 || IsMouseKeyName(name))
        {
            return false;
        }

        if (KeyAliases.TryGetValue(name, out vk))
        {
            return true;
        }

        // 虚拟键名兜底：只接受字母开头、由字母数字下划线组成的名称，避免 Enum.TryParse 把数字字符串当作枚举值
        if (!char.IsAsciiLetter(name[0]) || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            return false;
        }

        var enumName = name.StartsWith("vk_", StringComparison.OrdinalIgnoreCase) ? name : "VK_" + name;
        return Enum.TryParse(enumName, true, out vk) && Enum.IsDefined(vk);
    }

    /// <summary>
    /// 虚拟键的规范名称，写回脚本时使用
    /// </summary>
    public static string GetName(User32.VK vk)
    {
        if (CanonicalNames.TryGetValue(vk, out var name))
        {
            return name;
        }

        var enumName = vk.ToString();
        return (enumName.StartsWith("VK_", StringComparison.Ordinal) ? enumName[3..] : enumName).ToLowerInvariant();
    }

    /// <summary>
    /// 虚拟键的易读名称，界面上显示
    /// </summary>
    public static string GetDisplayName(User32.VK vk)
    {
        return DisplayNames.TryGetValue(vk, out var name) ? name : GetName(vk).ToUpperInvariant();
    }

    /// <summary>
    /// 解析鼠标键名
    /// </summary>
    /// <param name="name">鼠标键名</param>
    /// <param name="button">解析出的鼠标键</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParseButton(string name, out KeyMacroMouseButton button)
    {
        return ButtonAliases.TryGetValue(name.Trim(), out button);
    }

    /// <summary>
    /// 鼠标键的规范名称，写回脚本时使用
    /// </summary>
    public static string GetButtonName(KeyMacroMouseButton button) => button switch
    {
        KeyMacroMouseButton.Right => "right",
        KeyMacroMouseButton.Middle => "middle",
        KeyMacroMouseButton.X1 => "x1",
        KeyMacroMouseButton.X2 => "x2",
        _ => "left"
    };

    /// <summary>
    /// 鼠标键的易读名称，界面上显示
    /// </summary>
    public static string GetButtonDisplayName(KeyMacroMouseButton button) => button switch
    {
        KeyMacroMouseButton.Right => "右键",
        KeyMacroMouseButton.Middle => "中键",
        KeyMacroMouseButton.X1 => "侧键 1",
        KeyMacroMouseButton.X2 => "侧键 2",
        _ => "左键"
    };
}
