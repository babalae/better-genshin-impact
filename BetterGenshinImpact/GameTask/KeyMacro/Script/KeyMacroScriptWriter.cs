using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BetterGenshinImpact.GameTask.KeyMacro.Script;

/// <summary>
/// 把步骤写回脚本文本：一条指令一行，注释写成整行注释，备注写成行尾注释
/// </summary>
public static class KeyMacroScriptWriter
{
    /// <summary>
    /// 脚本换行符。宏文件按 Windows 习惯使用 CRLF
    /// </summary>
    public const string NewLine = "\r\n";

    /// <summary>
    /// 把步骤序列写成脚本
    /// </summary>
    public static string Write(IEnumerable<KeyMacroStep> steps)
    {
        return string.Join(NewLine, steps.Select(FormatLine));
    }

    /// <summary>
    /// 一个步骤对应的脚本行
    /// </summary>
    public static string FormatLine(KeyMacroStep step)
    {
        if (step.Kind == KeyMacroCommandKind.Comment)
        {
            return string.IsNullOrWhiteSpace(step.Text) ? "//" : "// " + step.Text.Trim();
        }

        var command = FormatCommand(step);
        return string.IsNullOrWhiteSpace(step.Note) ? command : $"{command}  // {step.Note.Trim()}";
    }

    /// <summary>
    /// 一条指令的规范写法，不含备注
    /// </summary>
    public static string FormatCommand(KeyMacroStep step)
    {
        var name = KeyMacroCommandCatalog.GetName(step.Kind);
        return step.Kind switch
        {
            KeyMacroCommandKind.KeyDown or KeyMacroCommandKind.KeyUp or KeyMacroCommandKind.KeyPress
                => $"{name}({KeyMacroKeys.GetName(step.Key)})",
            KeyMacroCommandKind.Click when step.HasPosition
                => $"{name}({KeyMacroKeys.GetButtonName(step.Button)}, {step.X}, {step.Y})",
            KeyMacroCommandKind.MouseDown or KeyMacroCommandKind.MouseUp or KeyMacroCommandKind.Click
                => $"{name}({KeyMacroKeys.GetButtonName(step.Button)})",
            KeyMacroCommandKind.MoveBy or KeyMacroCommandKind.MoveTo => $"{name}({step.X}, {step.Y})",
            KeyMacroCommandKind.Scroll => $"{name}({step.Amount})",
            KeyMacroCommandKind.Wait => $"{name}({FormatSeconds(step.WaitMs)})",
            _ => string.Empty
        };
    }

    /// <summary>
    /// 毫秒换算为秒，去掉末尾多余的 0（350 → 0.35）
    /// </summary>
    public static string FormatSeconds(int milliseconds)
    {
        return (milliseconds / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
