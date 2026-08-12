using BetterGenshinImpact.Core.Config;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterGenshinImpact.GameTask.CharacterDevelopment;

/// <summary>
/// 武器名称编辑距离匹配结果。
/// </summary>
/// <param name="Name">物品原型表中的标准武器名称。</param>
/// <param name="Distance">OCR 文本与标准名称的 Levenshtein 编辑距离。</param>
/// <param name="Similarity">按较长文本长度归一化后的相似度，范围为 0 到 1。</param>
/// <param name="IsReliable">匹配是否满足自动纠错的可信度要求。</param>
internal sealed record WeaponNameMatch(string Name, int Distance, double Similarity, bool IsReliable);

/// <summary>
/// 使用物品图标 ONNX 配套表格修正武器名称 OCR。
/// </summary>
/// <remarks>
/// <c>item.csv</c> 同时包含食物、材料和武器；只有 <c>item_class_id</c> 以 <c>weapon:</c>
/// 开头的记录属于武器。同一武器通常包含 normal/awaken 两条记录，加载时按名称去重。
/// </remarks>
internal static class WeaponNameMatcher
{
    private const int MaximumEditDistance = 1;
    private const double MinimumSimilarity = 2d / 3d;
    private const int MinimumDistanceMargin = 1;
    private const string WeaponPrototypePath = @"Assets\Model\ItemV2\item.csv";
    private static readonly Lazy<IReadOnlyList<string>> WeaponNames = new(LoadWeaponNames);

    /// <summary>
    /// 将 OCR 文本匹配为标准武器名称。名称表延迟加载且在进程内复用。
    /// </summary>
    public static WeaponNameMatch Match(string ocrText)
    {
        return MatchClosest(ocrText, WeaponNames.Value);
    }

    /// <summary>
    /// 在给定名称表中选择编辑距离最小的名称，并判断该候选是否足够可信。
    /// </summary>
    internal static WeaponNameMatch MatchClosest(string ocrText, IReadOnlyList<string> weaponNames)
    {
        var normalizedText = ocrText.Trim();
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            throw new InvalidOperationException("武器名称 OCR 结果为空。");
        }

        if (weaponNames.Count == 0)
        {
            throw new InvalidDataException("武器名称表中没有可用的武器。");
        }

        var candidates = weaponNames
            .Select(name =>
            {
                var distance = LevenshteinDistance(normalizedText, name);
                var maximumLength = Math.Max(normalizedText.Length, name.Length);
                var similarity = 1d - (double)distance / maximumLength;
                return new WeaponNameMatch(name, distance, similarity, false);
            })
            .OrderBy(match => match.Distance)
            .ThenBy(match => match.Name, StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        var best = candidates[0];
        var distanceMargin = candidates.Length == 1
            ? int.MaxValue
            : candidates[1].Distance - best.Distance;
        var isReliable = best.Distance <= MaximumEditDistance
                         && best.Similarity >= MinimumSimilarity
                         && (best.Distance == 0 || distanceMargin >= MinimumDistanceMargin);
        return best with { IsReliable = isReliable };
    }

    /// <summary>
    /// 从物品原型表中提取并去重标准武器名称。
    /// </summary>
    internal static IReadOnlyList<string> ExtractWeaponNames(
        IReadOnlyList<string> headers,
        IEnumerable<string[]> rows)
    {
        var itemClassIdIndex = FindRequiredColumn(headers, "item_class_id");
        var itemNameIndex = FindRequiredColumn(headers, "item_name");
        var requiredColumnCount = Math.Max(itemClassIdIndex, itemNameIndex) + 1;
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var columns in rows)
        {
            if (columns.Length < requiredColumnCount)
            {
                throw new InvalidDataException("物品原型表存在列数不足的记录。");
            }

            if (!columns[itemClassIdIndex].Trim().StartsWith("weapon:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = columns[itemNameIndex].Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        if (names.Count == 0)
        {
            throw new InvalidDataException("物品原型表中没有 item_class_id 以 weapon: 开头的武器记录。");
        }

        return names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 计算两个字符串的 Levenshtein 编辑距离。
    /// </summary>
    /// <remarks>仅保留两行动态规划状态，额外空间复杂度为 O(min(m,n))。</remarks>
    internal static int LevenshteinDistance(string source, string target)
    {
        if (source.Length == 0)
        {
            return target.Length;
        }

        if (target.Length == 0)
        {
            return source.Length;
        }

        if (source.Length > target.Length)
        {
            (source, target) = (target, source);
        }

        var previous = new int[source.Length + 1];
        var current = new int[source.Length + 1];
        for (var i = 0; i <= source.Length; i++)
        {
            previous[i] = i;
        }

        for (var targetIndex = 1; targetIndex <= target.Length; targetIndex++)
        {
            current[0] = targetIndex;
            for (var sourceIndex = 1; sourceIndex <= source.Length; sourceIndex++)
            {
                var substitutionCost = source[sourceIndex - 1] == target[targetIndex - 1] ? 0 : 1;
                current[sourceIndex] = Math.Min(
                    Math.Min(current[sourceIndex - 1] + 1, previous[sourceIndex] + 1),
                    previous[sourceIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[source.Length];
    }

    private static IReadOnlyList<string> LoadWeaponNames()
    {
        var path = Global.Absolute(WeaponPrototypePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("未找到物品 ONNX 配套表格。", path);
        }

        using var parser = new TextFieldParser(path, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields()
                      ?? throw new InvalidDataException("物品原型表缺少表头。");
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            rows.Add(parser.ReadFields()
                     ?? throw new InvalidDataException("物品原型表存在无法读取的记录。"));
        }

        return ExtractWeaponNames(headers, rows);
    }

    private static int FindRequiredColumn(IReadOnlyList<string> headers, string columnName)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i].Trim(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new InvalidDataException($"物品原型表缺少必需列 {columnName}。");
    }
}
