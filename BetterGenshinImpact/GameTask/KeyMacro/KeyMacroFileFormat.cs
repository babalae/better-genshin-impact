using BetterGenshinImpact.GameTask.KeyMacro.Script;
using BetterGenshinImpact.Model;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask.KeyMacro;

/// <summary>
/// 宏文件的内容：文件开头的设置和脚本
/// </summary>
/// <param name="Hotkey">快捷键字符串，空字符串表示未设置</param>
/// <param name="HotkeyType">快捷键类型</param>
/// <param name="TriggerMode">触发方式</param>
/// <param name="Enabled">是否启用</param>
/// <param name="Script">脚本（不含设置行）</param>
public sealed record KeyMacroFileContent(
    string Hotkey,
    HotKeyTypeEnum HotkeyType,
    KeyMacroTriggerMode TriggerMode,
    bool Enabled,
    string Script);

/// <summary>
/// 宏文件格式：开头连续的「// @键 值」行是设置，其余是脚本。设置行本身也是注释，整个文件仍是合法脚本
/// </summary>
public static partial class KeyMacroFileFormat
{
    /// <summary>
    /// 设置行：// @键 值
    /// </summary>
    [GeneratedRegex(@"^\s*//\s*@(\w+)\s*(.*?)\s*$")]
    private static partial Regex HeaderLineRegex();

    /// <summary>
    /// 解析文件内容。只识别 hotkey、hotkeyType、mode、enabled；遇到第一行不是这些设置的内容，脚本从这一行开始。
    /// 设置值无效时使用缺省值
    /// </summary>
    public static KeyMacroFileContent Parse(string content)
    {
        var hotkey = string.Empty;
        var hotkeyType = HotKeyTypeEnum.KeyboardMonitor;
        var mode = KeyMacroTriggerMode.Once;
        var enabled = true;

        var lines = content.TrimStart('\uFEFF').Split('\n');
        var bodyStart = 0;
        for (; bodyStart < lines.Length; bodyStart++)
        {
            var match = HeaderLineRegex().Match(lines[bodyStart].TrimEnd('\r'));
            if (!match.Success)
            {
                break;
            }

            var value = match.Groups[2].Value;
            var key = match.Groups[1].Value;
            if (key.Equals("hotkey", StringComparison.OrdinalIgnoreCase))
            {
                hotkey = NormalizeHotkey(value);
            }
            else if (key.Equals("hotkeyType", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse<HotKeyTypeEnum>(value, true, out var parsedType) && Enum.IsDefined(parsedType))
                {
                    hotkeyType = parsedType;
                }
            }
            else if (key.Equals("mode", StringComparison.OrdinalIgnoreCase))
            {
                mode = ParseMode(value) ?? mode;
            }
            else if (key.Equals("enabled", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(value, out var parsedEnabled))
                {
                    enabled = parsedEnabled;
                }
            }
            else
            {
                // 不认识的 @ 行属于脚本（普通注释），不吞掉用户内容
                break;
            }
        }

        var bodyLines = new List<string>();
        for (var i = bodyStart; i < lines.Length; i++)
        {
            bodyLines.Add(lines[i].TrimEnd('\r'));
        }

        // 文件末尾的换行不属于脚本
        while (bodyLines.Count > 0 && bodyLines[^1].Length == 0)
        {
            bodyLines.RemoveAt(bodyLines.Count - 1);
        }

        return new KeyMacroFileContent(hotkey, hotkeyType, mode, enabled, string.Join(KeyMacroScriptWriter.NewLine, bodyLines));
    }

    /// <summary>
    /// 生成文件内容：设置行 + 脚本，CRLF 换行，以换行结尾
    /// </summary>
    public static string Format(KeyMacroFileContent content)
    {
        var builder = new StringBuilder();
        builder.Append("// @hotkey");
        if (content.Hotkey.Length > 0)
        {
            builder.Append(' ').Append(content.Hotkey);
        }

        builder.Append(KeyMacroScriptWriter.NewLine);
        builder.Append("// @hotkeyType ").Append(content.HotkeyType).Append(KeyMacroScriptWriter.NewLine);
        builder.Append("// @mode ").Append(FormatMode(content.TriggerMode)).Append(KeyMacroScriptWriter.NewLine);
        builder.Append("// @enabled ").Append(content.Enabled ? "true" : "false").Append(KeyMacroScriptWriter.NewLine);

        var script = content.Script.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        if (script.Length > 0)
        {
            builder.Append(script.Replace("\n", KeyMacroScriptWriter.NewLine)).Append(KeyMacroScriptWriter.NewLine);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 从宏定义生成文件内容
    /// </summary>
    public static string Format(KeyMacroDefinition definition)
    {
        return Format(new KeyMacroFileContent(definition.Hotkey, definition.HotkeyType, definition.TriggerMode,
            definition.Enabled, definition.Script));
    }

    /// <summary>
    /// 触发方式在文件中的写法
    /// </summary>
    public static string FormatMode(KeyMacroTriggerMode mode) => mode switch
    {
        KeyMacroTriggerMode.Hold => "hold",
        KeyMacroTriggerMode.Toggle => "toggle",
        _ => "once"
    };

    /// <summary>
    /// 解析触发方式，无法识别时返回 null
    /// </summary>
    private static KeyMacroTriggerMode? ParseMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "once" => KeyMacroTriggerMode.Once,
        "hold" => KeyMacroTriggerMode.Hold,
        "toggle" => KeyMacroTriggerMode.Toggle,
        _ => null
    };

    /// <summary>
    /// 校验快捷键字符串：能解析时返回规范写法，不能解析时视为未设置
    /// </summary>
    private static string NormalizeHotkey(string value)
    {
        try
        {
            var hotKey = HotKey.FromString(value);
            return hotKey.IsEmpty ? string.Empty : hotKey.ToString();
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }
}
