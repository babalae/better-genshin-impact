using System;
using System.IO;
using BetterGenshinImpact.Core.Config;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.AutoFight.Strategy;

/// <summary>
/// 把战斗策略名称或路径解析为任务可用的策略路径。
/// 参数里既可以保存策略名（任务计划，便于跨机器），也可以保存路径（JS、旧入口）。
/// </summary>
public static class CombatStrategyResolver
{
    /// <summary>
    /// 自动按队伍选择策略的固定名称。
    /// </summary>
    public const string AutoSelectName = "根据队伍自动选择";

    /// <summary>
    /// 按队伍匹配限定子目录的前缀。
    /// </summary>
    public const string AutoScopePrefix = "auto:";

    /// <summary>键鼠录制策略的前缀。</summary>
    public const string KeyMousePrefix = "keymouse:";

    /// <summary>解析策略名称或路径；无效路径、越界引用和不可读文件均返回不存在的引用。</summary>
    public static CombatStrategyRef Parse(string? value)
    {
        var input = string.IsNullOrWhiteSpace(value) ? AutoSelectName : value;
        var kind = CombatStrategyKind.Txt;
        var path = input;
        try
        {
            var fightRoot = System.IO.Path.GetFullPath(Global.Absolute(@"User\AutoFight"));
            var macroRoot = System.IO.Path.GetFullPath(Global.Absolute(@"User\KeyMouseScript"));
            if (input.Equals(AutoFightParam.ComboStrategyName, StringComparison.Ordinal))
                return new(CombatStrategyKind.Combo, input, input, input, true);

            // 前缀必须先于路径判断；相对引用只允许访问所属根目录。
            if (input.Equals(AutoSelectName, StringComparison.Ordinal) || input.StartsWith(AutoScopePrefix, StringComparison.OrdinalIgnoreCase))
            {
                kind = CombatStrategyKind.AutoSelect;
                path = System.IO.Path.GetFullPath(System.IO.Path.Combine(fightRoot,
                    input == AutoSelectName ? "" : input[AutoScopePrefix.Length..]));
                if (!IsWithin(path, fightRoot)) return new(kind, input, input, input, false);
            }
            else if (input.StartsWith(KeyMousePrefix, StringComparison.OrdinalIgnoreCase))
            {
                kind = CombatStrategyKind.KeyMouse;
                var relative = input[KeyMousePrefix.Length..];
                path = System.IO.Path.GetFullPath(System.IO.Path.Combine(macroRoot,
                    relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? relative : relative + ".json"));
                if (!IsWithin(path, macroRoot)) return new(kind, input, input, input, false);
            }
            else if (System.IO.Path.IsPathFullyQualified(input))
            {
                // 已经是路径时直接检查存在性；根目录优先于内容识别，确保解析幂等。
                path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(input));
                kind = Directory.Exists(path) || !System.IO.Path.HasExtension(path)
                    ? CombatStrategyKind.AutoSelect : GetFileKind(path, fightRoot, macroRoot);
            }
            else
            {
                path = System.IO.Path.GetFullPath(System.IO.Path.Combine(fightRoot, input));
                if (!IsWithin(path, fightRoot)) return new(kind, input, input, input, false);
                var extension = System.IO.Path.GetExtension(path);
                if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    // 优先检测 .json，未命中则回退 .txt，兼容旧策略名称。
                    path += File.Exists(path + ".json") ? ".json" : ".txt";
                }
                kind = GetFileKind(path, fightRoot, macroRoot);
            }

            var canonical = path;
            var display = System.IO.Path.GetFileNameWithoutExtension(path);
            if (kind == CombatStrategyKind.AutoSelect && IsWithin(path, fightRoot))
            {
                var relative = System.IO.Path.GetRelativePath(fightRoot, path);
                canonical = relative == "." ? AutoSelectName : AutoScopePrefix + relative;
                display = relative == "." ? "按队伍匹配" : "按队伍匹配 · " + relative.Replace("\\", " / ");
            }
            else if (kind == CombatStrategyKind.KeyMouse && IsWithin(path, macroRoot))
            {
                var relative = System.IO.Path.GetRelativePath(macroRoot, path);
                var withoutExtension = System.IO.Path.ChangeExtension(relative, null);
                // 文件名本身以 .json 结尾时，保留实际扩展名才能唯一还原。
                canonical = KeyMousePrefix + (withoutExtension.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? relative : withoutExtension);
                display = withoutExtension.Replace("\\", " / ");
            }
            else if (kind is CombatStrategyKind.Txt or CombatStrategyKind.Json && IsWithin(path, fightRoot))
            {
                var relative = System.IO.Path.GetRelativePath(fightRoot, path);
                var withoutExtension = System.IO.Path.ChangeExtension(relative, null);
                var nameExtension = System.IO.Path.GetExtension(withoutExtension);
                var requiresExtension = withoutExtension is AutoSelectName or AutoFightParam.ComboStrategyName
                    || nameExtension.Equals(".txt", StringComparison.OrdinalIgnoreCase) || nameExtension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                    || kind == CombatStrategyKind.Txt && File.Exists(System.IO.Path.ChangeExtension(path, ".json"));
                canonical = requiresExtension ? relative : withoutExtension;
                display = withoutExtension.Replace("\\", " / ");
            }

            var exists = kind == CombatStrategyKind.AutoSelect ? Directory.Exists(path)
                : kind != CombatStrategyKind.None && File.Exists(path);
            // 前缀要求的文件/目录类型不符时保留原引用，避免构造函数写回路径后被二次解析成其他类型。
            if (!exists && (File.Exists(path) || Directory.Exists(path)))
                return new(kind, input, input, input, false);
            return new(kind, path, canonical, display, exists);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            return new(kind, path, input, input, false);
        }
    }

    /// <summary>
    /// 解析策略名称或路径，策略不存在时抛出异常。
    /// </summary>
    /// <param name="nameOrPath">策略名称或路径。</param>
    /// <returns>解析后的策略引用。</returns>
    /// <exception cref="FileNotFoundException">策略不存在。</exception>
    public static CombatStrategyRef Resolve(string? nameOrPath)
    {
        var strategy = Parse(nameOrPath);
        return strategy.Exists ? strategy : throw new FileNotFoundException("战斗策略不存在。", strategy.Path);
    }

    /// <summary>按路径边界判断归属，避免同名前缀目录及相对引用越界。</summary>
    private static bool IsWithin(string path, string root)
        => path.Equals(root, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>区分文本、条件策略与录制文件；外部 JSON 只流式检查顶层属性名。</summary>
    private static CombatStrategyKind GetFileKind(string path, string fightRoot, string macroRoot)
    {
        if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return CombatStrategyKind.Txt;
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return CombatStrategyKind.None;
        if (IsWithin(path, macroRoot)) return CombatStrategyKind.KeyMouse;
        if (IsWithin(path, fightRoot) || !File.Exists(path)) return CombatStrategyKind.Json;

        using var stream = File.OpenText(path);
        using var reader = new JsonTextReader(stream);
        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.PropertyName && reader.Depth == 1
                && string.Equals(reader.Value as string, "macroEvents", StringComparison.Ordinal))
                return CombatStrategyKind.KeyMouse;
            // 跳过属性值，嵌套对象中的同名属性不参与类型判断。
            if (reader.TokenType == JsonToken.PropertyName && reader.Depth == 1)
            {
                reader.Read();
                reader.Skip();
            }
        }
        return CombatStrategyKind.Json;
    }
}
