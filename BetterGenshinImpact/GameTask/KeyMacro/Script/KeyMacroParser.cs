using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.KeyMacro.Script;

/// <summary>
/// 按键宏脚本解析器。独立实现，不依赖战斗策略的解析代码。
/// 书写规则：一行一条或多条指令（用 , 或 ; 分隔），整行 // 或 # 为注释，行尾 // 为备注，不区分大小写
/// </summary>
public static class KeyMacroParser
{
    /// <summary>
    /// wait 允许的最长秒数
    /// </summary>
    public const double MaxWaitSeconds = 3600;

    /// <summary>
    /// 绝对坐标所用游戏画面的宽度。与 GameRegion1080PPosMove 和 JS 脚本的 moveMouseTo 一致，以 1920×1080 为准
    /// </summary>
    public const int GameWidth = 1920;

    /// <summary>
    /// 绝对坐标所用游戏画面的高度
    /// </summary>
    public const int GameHeight = 1080;

    /// <summary>
    /// 战斗策略中的包装指令和别名。按键宏不支持它们，出现时给出明确提示而不是“未知指令”
    /// </summary>
    private static readonly HashSet<string> CombatCommandNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "attack", "charge", "walk", "w", "a", "s", "d", "dash", "jump", "j", "skill", "e", "burst", "q",
        "round", "ready", "check", "aim", "r", "普攻", "普通攻击", "重击", "行走", "冲刺", "跳跃", "完成", "检测", "瞄准"
    };

    /// <summary>
    /// 战斗策略中基础指令的别名 → 按键宏中的写法
    /// </summary>
    private static readonly Dictionary<string, string> RenamedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["after"] = "wait",
        ["等待"] = "wait",
        ["verticalscroll"] = "scroll"
    };

    /// <summary>
    /// 解析脚本
    /// </summary>
    /// <param name="script">脚本文本，可以为 null</param>
    /// <param name="options">解析选项，为 null 时不做快捷键相关检查</param>
    /// <returns>解析结果</returns>
    public static KeyMacroDocument Parse(string? script, KeyMacroParseOptions? options = null)
    {
        options ??= KeyMacroParseOptions.Default;
        if (string.IsNullOrEmpty(script))
        {
            var empty = new List<KeyMacroDiagnostic>();
            CheckDocument([], empty, options);
            return new KeyMacroDocument([], empty);
        }

        var steps = new List<KeyMacroStep>();
        var diagnostics = new List<KeyMacroDiagnostic>();
        var lines = script.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            ParseLine(lines[i].TrimEnd('\r'), i + 1, steps, diagnostics, options);
        }

        CheckDocument(steps, diagnostics, options);
        return new KeyMacroDocument(steps, diagnostics);
    }

    /// <summary>
    /// 解析一行：拆出注释，按 , 和 ; 拆分指令，逐条解析
    /// </summary>
    private static void ParseLine(string rawLine, int lineNumber, List<KeyMacroStep> steps,
        List<KeyMacroDiagnostic> diagnostics, KeyMacroParseOptions options)
    {
        // 1. 全角符号逐字替换为半角，长度不变，列位置与原文一致
        var line = Normalize(rawLine);

        // 2. 拆出注释：# 只作整行注释；// 之后都是注释（// 不会出现在任何参数里）
        string? comment = null;
        var code = line;
        if (line.TrimStart().StartsWith('#'))
        {
            comment = line.TrimStart()[1..].Trim();
            code = string.Empty;
        }
        else
        {
            var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                comment = line[(commentIndex + 2)..].Trim();
                code = line[..commentIndex];
            }
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            if (comment != null)
            {
                steps.Add(new KeyMacroStep
                {
                    Kind = KeyMacroCommandKind.Comment,
                    Text = comment,
                    Line = lineNumber,
                    Column = 0,
                    Length = line.Length
                });
            }

            return;
        }

        // 3. 逐条解析指令，行尾注释作为本行最后一条有效指令的备注
        var lastStepIndex = -1;
        foreach (var (start, text) in SplitTokens(code))
        {
            var step = ParseToken(text, lineNumber, start, diagnostics, options);
            if (step != null)
            {
                steps.Add(step);
                lastStepIndex = steps.Count - 1;
            }
        }

        if (!string.IsNullOrEmpty(comment) && lastStepIndex >= 0)
        {
            steps[lastStepIndex] = steps[lastStepIndex] with { Note = comment };
        }
    }

    /// <summary>
    /// 全角括号、逗号、分号替换为半角
    /// </summary>
    private static string Normalize(string line)
    {
        return line.Replace('（', '(').Replace('）', ')').Replace('，', ',').Replace('；', ';');
    }

    /// <summary>
    /// 按括号外的 , 和 ; 拆分指令，返回每条指令去掉首尾空白后的起始列和文本，跳过空段
    /// </summary>
    private static IEnumerable<(int Start, string Text)> SplitTokens(string code)
    {
        var depth = 0;
        var segmentStart = 0;
        for (var i = 0; i <= code.Length; i++)
        {
            var atEnd = i == code.Length;
            if (!atEnd)
            {
                var c = code[i];
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')' && depth > 0)
                {
                    depth--;
                }

                if (depth != 0 || (c != ',' && c != ';'))
                {
                    continue;
                }
            }

            var raw = code[segmentStart..i];
            var text = raw.Trim();
            if (text.Length > 0)
            {
                yield return (segmentStart + (raw.Length - raw.TrimStart().Length), text);
            }

            segmentStart = i + 1;
        }
    }

    /// <summary>
    /// 解析一条指令。无法识别时记录错误并返回 null
    /// </summary>
    private static KeyMacroStep? ParseToken(string text, int line, int column, List<KeyMacroDiagnostic> diagnostics,
        KeyMacroParseOptions options)
    {
        void Error(string message) =>
            diagnostics.Add(new KeyMacroDiagnostic(KeyMacroDiagnosticSeverity.Error, line, column, text.Length, message));

        // 拆出指令名和参数
        string name;
        string? argText = null;
        var open = text.IndexOf('(');
        if (open < 0)
        {
            if (text.Contains(')'))
            {
                Error("括号不完整，缺少「(」");
                return null;
            }

            name = text;
        }
        else
        {
            var close = text.IndexOf(')', open);
            if (close < 0)
            {
                Error("括号不完整，缺少「)」");
                return null;
            }

            if (close < text.Length - 1)
            {
                Error("多条指令之间要用逗号分隔或换行");
                return null;
            }

            if (text.IndexOf('(', open + 1) >= 0)
            {
                Error("括号不能嵌套");
                return null;
            }

            name = text[..open].Trim();
            argText = text[(open + 1)..close];
        }

        if (name.Length == 0)
        {
            Error("缺少指令名");
            return null;
        }

        if (name.Any(char.IsWhiteSpace))
        {
            Error("多条指令之间要用逗号分隔或换行");
            return null;
        }

        if (!KeyMacroCommandCatalog.TryGet(name, out var info))
        {
            Error(GetUnknownCommandMessage(name));
            return null;
        }

        var args = string.IsNullOrWhiteSpace(argText)
            ? []
            : argText.Split(',').Select(a => a.Trim()).ToArray();
        var step = new KeyMacroStep { Kind = info.Kind, Line = line, Column = column, Length = text.Length };

        // 按指令类型校验参数
        switch (info.Kind)
        {
            case KeyMacroCommandKind.KeyDown:
            case KeyMacroCommandKind.KeyUp:
            case KeyMacroCommandKind.KeyPress:
            {
                if (args.Length != 1 || args[0].Length == 0)
                {
                    Error($"{info.Name} 需要一个按键，例如 {info.Example}");
                    return null;
                }

                if (KeyMacroKeys.IsMouseKeyName(args[0]))
                {
                    Error("鼠标键请用 click、mousedown、mouseup 指令");
                    return null;
                }

                if (!KeyMacroKeys.TryParseKey(args[0], out var vk))
                {
                    Error($"无法识别的按键「{args[0]}」");
                    return null;
                }

                if (options.HotkeyKey == vk)
                {
                    Error($"不能包含自己的快捷键 {KeyMacroKeys.GetDisplayName(vk)}，宏发出的按键会再次触发自己");
                    return null;
                }

                return step with { Key = vk };
            }
            case KeyMacroCommandKind.MouseDown:
            case KeyMacroCommandKind.MouseUp:
            case KeyMacroCommandKind.Click:
            {
                // click 可以写 click(鼠标键, x, y)；mousedown、mouseup 只有鼠标键
                var isClick = info.Kind == KeyMacroCommandKind.Click;
                if (isClick ? (args.Length is 2 or > 3) : args.Length > 1)
                {
                    Error(isClick
                        ? "click 的参数是鼠标键，或鼠标键加坐标，例如 click(left) 或 click(left, 960, 540)"
                        : $"{info.Name} 只能有一个鼠标键，例如 {info.Example}");
                    return null;
                }

                var button = KeyMacroMouseButton.Left;
                if (args.Length >= 1 && !KeyMacroKeys.TryParseButton(args[0], out button))
                {
                    Error($"无法识别的鼠标键「{args[0]}」，可用 left、right、middle、x1、x2");
                    return null;
                }

                if (options.HotkeyButton == button)
                {
                    Error($"不能包含自己的快捷键 {KeyMacroKeys.GetButtonDisplayName(button)}，宏发出的按键会再次触发自己");
                    return null;
                }

                if (args.Length < 3)
                {
                    return step with { Button = button };
                }

                if (!TryParsePosition(args[1], args[2], "click(left, 960, 540)", out var clickX, out var clickY, out var clickError))
                {
                    Error(clickError);
                    return null;
                }

                return step with { Button = button, HasPosition = true, X = clickX, Y = clickY };
            }
            case KeyMacroCommandKind.MoveBy:
            {
                if (args.Length != 2 || !TryParseInt(args[0], out var x) || !TryParseInt(args[1], out var y))
                {
                    Error("moveby 需要两个整数，例如 moveby(100, 0)");
                    return null;
                }

                return step with { X = x, Y = y };
            }
            case KeyMacroCommandKind.MoveTo:
            {
                if (args.Length != 2)
                {
                    Error("moveto 需要两个整数坐标，例如 moveto(960, 540)");
                    return null;
                }

                if (!TryParsePosition(args[0], args[1], "moveto(960, 540)", out var x, out var y, out var moveError))
                {
                    Error(moveError);
                    return null;
                }

                return step with { X = x, Y = y };
            }
            case KeyMacroCommandKind.Scroll:
            {
                if (args.Length != 1 || !TryParseInt(args[0], out var amount))
                {
                    Error("scroll 需要一个整数，正数向上，例如 scroll(1)");
                    return null;
                }

                if (amount == 0)
                {
                    Error("scroll 的格数不能为 0");
                    return null;
                }

                return step with { Amount = amount };
            }
            case KeyMacroCommandKind.Wait:
            {
                if (args.Length != 1
                    || !double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    || !double.IsFinite(seconds))
                {
                    Error("wait 需要一个秒数，例如 wait(0.5)");
                    return null;
                }

                if (seconds < 0 || seconds > MaxWaitSeconds)
                {
                    Error($"wait 的秒数要在 0 到 {MaxWaitSeconds} 之间");
                    return null;
                }

                return step with { WaitMs = (int)Math.Round(seconds * 1000) };
            }
            default:
                Error($"未知指令 {name}");
                return null;
        }
    }

    /// <summary>
    /// 解析整数参数
    /// </summary>
    private static bool TryParseInt(string text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// 解析游戏画面坐标：两个整数，范围 0–1920、0–1080（与 JS 脚本 moveMouseTo 的范围检查一致）
    /// </summary>
    /// <param name="xText">横坐标文本</param>
    /// <param name="yText">纵坐标文本</param>
    /// <param name="example">出错时提示的示例写法</param>
    /// <param name="x">横坐标</param>
    /// <param name="y">纵坐标</param>
    /// <param name="error">失败原因</param>
    private static bool TryParsePosition(string xText, string yText, string example, out int x, out int y, out string error)
    {
        y = 0;
        if (!TryParseInt(xText, out x) || !TryParseInt(yText, out y))
        {
            error = $"坐标需要两个整数，例如 {example}";
            return false;
        }

        if (x < 0 || x > GameWidth || y < 0 || y > GameHeight)
        {
            error = $"坐标要在游戏画面 {GameWidth}×{GameHeight} 内：x 为 0–{GameWidth}，y 为 0–{GameHeight}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 未知指令的提示：战斗策略指令说明不支持；拼写接近时给出建议
    /// </summary>
    private static string GetUnknownCommandMessage(string name)
    {
        if (RenamedCommands.TryGetValue(name, out var renamed))
        {
            return $"按键宏里请用 {renamed} 代替 {name}";
        }

        if (CombatCommandNames.Contains(name))
        {
            return $"不支持战斗策略指令 {name}，按键宏只支持基础键鼠指令";
        }

        var lower = name.ToLowerInvariant();
        var suggestion = KeyMacroCommandCatalog.All
            .Select(c => (c.Name, Distance: GetEditDistance(lower, c.Name)))
            .Where(c => c.Distance <= 2)
            .OrderBy(c => c.Distance)
            .Select(c => c.Name)
            .FirstOrDefault();
        return suggestion != null ? $"未知指令 {name}，是否要写 {suggestion}？" : $"未知指令 {name}";
    }

    /// <summary>
    /// 编辑距离（Levenshtein），用于拼写建议
    /// </summary>
    private static int GetEditDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>
    /// 整体检查：没有指令、循环模式缺少等待、按下后没有松开
    /// </summary>
    private static void CheckDocument(List<KeyMacroStep> steps, List<KeyMacroDiagnostic> diagnostics, KeyMacroParseOptions options)
    {
        var commands = steps.Where(s => s.IsCommand).ToList();
        if (commands.Count == 0)
        {
            if (!diagnostics.Any(d => d.IsError))
            {
                diagnostics.Add(new KeyMacroDiagnostic(KeyMacroDiagnosticSeverity.Warning, 0, 0, 0, "还没有任何指令"));
            }

            return;
        }

        if (options.Mode != KeyMacroTriggerMode.Once && commands.All(c => c.Kind != KeyMacroCommandKind.Wait || c.WaitMs == 0))
        {
            diagnostics.Add(new KeyMacroDiagnostic(KeyMacroDiagnosticSeverity.Warning, 0, 0, 0,
                "循环模式下每轮没有等待，执行时每轮之间只间隔 10 毫秒"));
        }

        // 按顺序模拟按下状态，找出到结尾仍未松开的键
        var heldKeys = new Dictionary<User32.VK, KeyMacroStep>();
        var heldButtons = new Dictionary<KeyMacroMouseButton, KeyMacroStep>();
        foreach (var command in commands)
        {
            switch (command.Kind)
            {
                case KeyMacroCommandKind.KeyDown:
                    heldKeys.TryAdd(command.Key, command);
                    break;
                case KeyMacroCommandKind.KeyUp:
                case KeyMacroCommandKind.KeyPress:
                    heldKeys.Remove(command.Key);
                    break;
                case KeyMacroCommandKind.MouseDown:
                    heldButtons.TryAdd(command.Button, command);
                    break;
                case KeyMacroCommandKind.MouseUp:
                case KeyMacroCommandKind.Click:
                    heldButtons.Remove(command.Button);
                    break;
            }
        }

        foreach (var (key, step) in heldKeys)
        {
            diagnostics.Add(new KeyMacroDiagnostic(KeyMacroDiagnosticSeverity.Warning, step.Line, step.Column, step.Length,
                $"{KeyMacroKeys.GetDisplayName(key)} 按下后没有松开，宏结束或停止时会自动松开"));
        }

        foreach (var (button, step) in heldButtons)
        {
            diagnostics.Add(new KeyMacroDiagnostic(KeyMacroDiagnosticSeverity.Warning, step.Line, step.Column, step.Length,
                $"鼠标{KeyMacroKeys.GetButtonDisplayName(button)}按下后没有松开，宏结束或停止时会自动松开"));
        }
    }
}
