using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>
/// 白名单模式的 OCR 单字模糊匹配规则
/// </summary>
/// <param name="Pattern">匹配模式，其中的 〇 恰好匹配一个任意字</param>
/// <param name="Text">准确名称，拾取后日志按它输出</param>
public sealed record AutoPickFuzzyRule(string Pattern, string Text);

/// <summary>
/// 白名单模式的 OCR 单字模糊匹配器。
/// 按 〇 的位置把 OCR 文本改写成查找键，再做一次哈希查找判断是否命中，
/// 成本只与该长度下不同的 〇 位置组合数有关，与规则条数无关，匹配过程没有堆分配。
/// 构建后只读，可以直接在截图线程上使用。
/// </summary>
/// <remarks>
/// 暂不处理的场景：
/// 1. 认错导致字数变化（一个字拆成两个、漏字），〇 只匹配恰好一个字；
/// 2. 〇 在首尾位置时，若 OCR 把该字认成非汉字，ProcessOcrText 会把它裁掉，长度变化后不会命中；
/// 3. 扩展 B 区等代理对汉字按 UTF-16 字符比较，现有 ProcessOcrText 也只处理基本区汉字。
/// </remarks>
public sealed class AutoPickFuzzyMatcher
{
    /// <summary>
    /// 单字通配符，游戏正文不会出现
    /// </summary>
    public const char Wildcard = '〇';

    /// <summary>
    /// 规则最大长度，保证匹配时 stackalloc 的缓冲区足够小
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// 没有任何规则的匹配器
    /// </summary>
    public static AutoPickFuzzyMatcher Empty { get; } = new([]);

    /// <summary>
    /// 以匹配模式为键的规则表，用 span 查找，不分配字符串
    /// </summary>
    private readonly FrozenDictionary<string, AutoPickFuzzyRule>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    /// <summary>
    /// 下标为文本长度，值为该长度下不同的 〇 位置组合，按 〇 数量升序
    /// </summary>
    private readonly int[][][] _wildcardPositionsByLength;

    /// <summary>
    /// 构建匹配器，规则须先经过 <see cref="Validate"/> 校验
    /// </summary>
    /// <param name="rules">已校验的规则，匹配模式相同时只保留第一条</param>
    public AutoPickFuzzyMatcher(IEnumerable<AutoPickFuzzyRule> rules)
    {
        // 1. 按匹配模式去重，保留文件中的先后顺序
        var uniqueRules = new List<AutoPickFuzzyRule>();
        var patterns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (patterns.Add(rule.Pattern))
            {
                uniqueRules.Add(rule);
            }
        }

        // 2. 收集每个长度下不同的 〇 位置组合
        var maxLength = uniqueRules.Count == 0 ? 0 : uniqueRules.Max(rule => rule.Pattern.Length);
        var groupsByLength = new List<int[]>?[maxLength + 1];
        foreach (var rule in uniqueRules)
        {
            var positions = GetWildcardPositions(rule.Pattern);
            var groups = groupsByLength[rule.Pattern.Length] ??= [];
            if (!groups.Any(group => group.AsSpan().SequenceEqual(positions)))
            {
                groups.Add(positions);
            }
        }

        // 3. 〇 少的组合先查，使更具体的规则优先；OrderBy 是稳定排序，数量相同时保持文件顺序
        _wildcardPositionsByLength = groupsByLength
            .Select(groups => groups is null ? Array.Empty<int[]>() : groups.OrderBy(group => group.Length).ToArray())
            .ToArray();

        _lookup = uniqueRules
            .ToFrozenDictionary(rule => rule.Pattern, StringComparer.Ordinal)
            .GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>
    /// 校验一条规则
    /// </summary>
    /// <param name="pattern">匹配模式</param>
    /// <param name="text">准确名称</param>
    /// <returns>不合法的原因；合法时返回 null</returns>
    public static string? Validate(string? pattern, string? text)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text))
        {
            return "匹配模式和准确名称不能为空";
        }

        if (pattern.Length != text.Length)
        {
            return "匹配模式和准确名称的字数不同";
        }

        if (pattern.Length > MaxLength)
        {
            return $"字数超过 {MaxLength}";
        }

        // 逐字检查：〇 位置任意，其余位置必须与准确名称一致
        var wildcardCount = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (text[i] == Wildcard)
            {
                return $"准确名称中不能包含 {Wildcard}";
            }

            if (pattern[i] == Wildcard)
            {
                wildcardCount++;
            }
            else if (pattern[i] != text[i])
            {
                return $"第 {i + 1} 个字与准确名称不一致";
            }
        }

        if (wildcardCount == 0)
        {
            return $"匹配模式中没有 {Wildcard}";
        }

        // 全是 〇 会匹配任意同字数的文本
        if (wildcardCount == pattern.Length)
        {
            return "匹配模式至少要保留一个确定的字";
        }

        return null;
    }

    /// <summary>
    /// 尝试用模糊规则匹配 OCR 文本
    /// </summary>
    /// <param name="text">经过 ProcessOcrText 处理后的 OCR 文本</param>
    /// <param name="rule">命中的规则</param>
    /// <returns>是否命中</returns>
    public bool TryMatch(string text, [NotNullWhen(true)] out AutoPickFuzzyRule? rule)
    {
        rule = null;

        // 大多数文本在这里返回：长度超出范围，或该长度没有规则
        if ((uint)text.Length >= (uint)_wildcardPositionsByLength.Length)
        {
            return false;
        }

        var positionGroups = _wildcardPositionsByLength[text.Length];
        if (positionGroups.Length == 0)
        {
            return false;
        }

        // 长度不超过 MaxLength，栈上分配即可
        Span<char> key = stackalloc char[text.Length];
        foreach (var positions in positionGroups)
        {
            // 把 OCR 文本对应位置换成 〇，得到的串如果正好是某条规则的匹配模式，就算命中
            text.AsSpan().CopyTo(key);
            foreach (var position in positions)
            {
                key[position] = Wildcard;
            }

            if (_lookup.TryGetValue(key, out var matchedRule))
            {
                rule = matchedRule;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 取出匹配模式中所有 〇 的位置
    /// </summary>
    private static int[] GetWildcardPositions(string pattern)
    {
        var positions = new List<int>();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == Wildcard)
            {
                positions.Add(i);
            }
        }

        return positions.ToArray();
    }
}
