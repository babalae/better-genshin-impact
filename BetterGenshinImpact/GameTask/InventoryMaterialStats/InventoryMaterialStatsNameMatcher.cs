using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BetterGenshinImpact.GameTask.InventoryMaterialStats;

/// <summary>
/// 将格子名称 OCR 对齐到模板文件名。
/// </summary>
public static class InventoryMaterialStatsNameMatcher
{
    private const double MinSimilarity = 0.85;

    private static readonly Dictionary<char, char> NearCharMap = new()
    {
        ['监'] = '盐',
        ['炽'] = '烬',
        ['盞'] = '盏',
        ['攜'] = '携',
        ['於'] = '于',
        ['卵'] = '卯',
        ['亥'] = '刻',
        ['脈'] = '脉',
        ['黄'] = '夤',
        ['黃'] = '夤',
        ['问'] = '间',
        ['谭'] = '镡',
        ['凈'] = '净',
        ['淨'] = '净',
        ['靑'] = '青',
    };

    private static readonly Dictionary<string, string> NameAliases = new(StringComparer.Ordinal)
    {
        // 点开 OCR 常把「柽木」认成「怪木」，会当成第二种木头把同一格计两次。
        ["怪木"] = "柽木",
    };

    /// <summary>
    /// 去掉 OCR 标题里的空格（半角、全角、换行等），例如「证悟 木」→「证悟木」。
    /// </summary>
    public static string StripWhiteSpace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch) && ch != '\u3000')
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 白名单文件名：纠形近字后再做整词别名。
    /// </summary>
    public static string Canonical(string? text)
    {
        var normalized = Normalize(text ?? string.Empty);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return NameAliases.GetValueOrDefault(normalized, normalized);
    }

    public static string Normalize(string text)
    {
        var stripped = StripWhiteSpace(text);
        if (stripped.Length == 0)
        {
            return string.Empty;
        }

        var nfkc = stripped.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(nfkc.Length);
        foreach (var ch in nfkc)
        {
            if (char.IsWhiteSpace(ch) || ch == '\u3000')
            {
                continue;
            }

            sb.Append(NearCharMap.GetValueOrDefault(ch, ch));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 在候选中找唯一最高分且超过阈值的名称；打平或不够像则返回 null。
    /// OCR 多出后缀（如「枫木中」）时，再按最长前缀对齐到白名单（「枫木」）。
    /// 全称已在目录里时不要截短：苹果酿不能对成苹果。
    /// </summary>
    public static string? MatchUnique(
        string ocrText,
        IEnumerable<string> candidates,
        IEnumerable<string>? catalog = null)
    {
        var list = candidates as IList<string> ?? candidates.ToList();
        var catalogList = catalog == null
            ? list
            : catalog as IList<string> ?? catalog.ToList();
        var normalizedOcr = Canonical(ocrText);
        if (normalizedOcr.Length == 0 || list.Count == 0)
        {
            return null;
        }

        foreach (var name in catalogList)
        {
            if (string.Equals(Canonical(name), normalizedOcr, StringComparison.Ordinal))
            {
                return Canonical(name);
            }
        }

        string? bestName = null;
        var bestScore = 0d;
        var tied = false;
        foreach (var candidate in list)
        {
            var score = Similarity(normalizedOcr, Canonical(candidate));
            if (score > bestScore + 1e-9)
            {
                bestScore = score;
                bestName = candidate;
                tied = false;
            }
            else if (Math.Abs(score - bestScore) <= 1e-9 && bestName != null &&
                     !string.Equals(Canonical(bestName), Canonical(candidate), StringComparison.Ordinal))
            {
                tied = true;
            }
        }

        if (!tied && bestName != null && bestScore >= MinSimilarity)
        {
            return Canonical(bestName);
        }

        var prefixed = MatchLongestPrefix(normalizedOcr, list, catalogList);
        return prefixed == null ? null : Canonical(prefixed);
    }

    /// <summary>
    /// 目录里是否还有以该名为前缀的更长物品（苹果 vs 苹果酿）。
    /// </summary>
    public static bool HasLongerName(string name, IEnumerable<string> names)
    {
        var prefix = Canonical(name);
        if (prefix.Length < 2)
        {
            return false;
        }

        return names.Any(n =>
        {
            var canonical = Canonical(n);
            return canonical.Length > prefix.Length &&
                   canonical.StartsWith(prefix, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// OCR 以某候选名为前缀且多了尾巴时，取最长且唯一的那个候选。
    /// </summary>
    private static string? MatchLongestPrefix(
        string normalizedOcr,
        IList<string> candidates,
        IList<string> catalog)
    {
        // 全称已是目录里的物品时，禁止截成短前缀。
        if (catalog.Any(c => string.Equals(Canonical(c), normalizedOcr, StringComparison.Ordinal)))
        {
            return null;
        }

        string? bestName = null;
        var bestLen = 0;
        var tied = false;
        foreach (var candidate in candidates)
        {
            var normalized = Canonical(candidate);
            if (normalized.Length < 2 ||
                !normalizedOcr.StartsWith(normalized, StringComparison.Ordinal))
            {
                continue;
            }

            if (normalized.Length > bestLen)
            {
                bestLen = normalized.Length;
                bestName = candidate;
                tied = false;
            }
            else if (normalized.Length == bestLen && bestName != null &&
                     !string.Equals(Canonical(bestName), Canonical(candidate), StringComparison.Ordinal))
            {
                tied = true;
            }
        }

        return tied ? null : bestName;
    }

    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1;
        }

        var max = Math.Max(a.Length, b.Length);
        if (max == 0)
        {
            return 0;
        }

        return 1d - (Levenshtein(a, b) / (double)max);
    }

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= m; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
