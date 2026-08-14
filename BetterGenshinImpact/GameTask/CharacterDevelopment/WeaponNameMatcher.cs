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
/// <param name="IsReliable">匹配是否满足自动纠错的可信度要求。</param>
internal sealed record WeaponNameMatch(string Name, int Distance, bool IsReliable);

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
    private const string WeaponPrototypePath = @"Assets\Model\ItemV2\item.csv";
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>> WeaponNamesByType =
        new(LoadWeaponNames);

    /// <summary>
    /// 将 OCR 文本匹配为标准武器名称。名称表延迟加载且在进程内复用。
    /// </summary>
    public static WeaponNameMatch Match(string ocrText, string weaponType)
    {
        return MatchClosest(ocrText, weaponType, WeaponNamesByType.Value);
    }

    /// <summary>
    /// 在给定名称表中选择编辑距离最小的名称，并判断该候选是否足够可信。
    /// </summary>
    internal static WeaponNameMatch MatchClosest(
        string ocrText,
        string weaponType,
        IReadOnlyDictionary<string, IReadOnlyList<string>> weaponNamesByType)
    {
        var normalizedText = NormalizeWeaponName(ocrText.Trim());
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            throw new InvalidOperationException("武器名称 OCR 结果为空。");
        }

        if (string.IsNullOrWhiteSpace(weaponType)
            || !weaponNamesByType.TryGetValue(weaponType, out var weaponNames))
        {
            throw new InvalidDataException($"武器名称表中没有类型为 {weaponType} 的武器。");
        }

        if (weaponNames.Count == 0)
        {
            throw new InvalidDataException($"武器名称表中类型为 {weaponType} 的武器列表为空。");
        }

        var candidates = weaponNames
            .Select(name =>
            {
                var normalizedName = NormalizeWeaponName(name);
                var distance = LevenshteinDistance(normalizedText, normalizedName);
                return new
                {
                    Match = new WeaponNameMatch(name, distance, false),
                    IsSameLength = normalizedName.Length == normalizedText.Length
                };
            })
            .OrderBy(candidate => candidate.Match.Distance)
            .ThenBy(candidate => candidate.Match.Name, StringComparer.Ordinal)
            .ToArray();

        var bestSameLength = candidates.FirstOrDefault(candidate => candidate.IsSameLength);
        if (bestSameLength == null)
        {
            return candidates[0].Match;
        }

        return bestSameLength.Match with
        {
            IsReliable = bestSameLength.Match.Distance <= MaximumEditDistance
        };
    }

    /// <summary>
    /// 从物品原型表中提取并去重标准武器名称。
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ExtractWeaponNames(
        IReadOnlyList<string> headers,
        IEnumerable<string[]> rows)
    {
        var itemClassIdIndex = FindRequiredColumn(headers, "item_class_id");
        var itemNameIndex = FindRequiredColumn(headers, "item_name");
        var weaponTypeIndex = FindRequiredColumn(headers, "weapon_type");
        var requiredColumnCount = Math.Max(itemClassIdIndex, Math.Max(itemNameIndex, weaponTypeIndex)) + 1;
        var namesByType = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var columns in rows)
        {
            if (columns.Length < requiredColumnCount)
            {
                throw new InvalidDataException("物品原型表存在列数不足的记录。");
            }

            var itemClassId = columns[itemClassIdIndex].Trim();
            if (!itemClassId.StartsWith("weapon:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var weaponType = columns[weaponTypeIndex].Trim();
            if (string.IsNullOrWhiteSpace(weaponType))
            {
                throw new InvalidDataException("物品原型表中的武器记录缺少 weapon_type。");
            }

            var name = columns[itemNameIndex].Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                if (!namesByType.TryGetValue(weaponType, out var names))
                {
                    names = new HashSet<string>(StringComparer.Ordinal);
                    namesByType.Add(weaponType, names);
                }

                names.Add(name);
            }
        }

        if (namesByType.Count == 0)
        {
            throw new InvalidDataException("物品原型表中没有 item_class_id 以 weapon: 开头的武器记录。");
        }

        return namesByType.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// 将 OCR 中常见的繁体及日文异体字统一为武器表使用的简体字。
    /// </summary>
    internal static string NormalizeWeaponName(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            //因为对比的前提条件是相同武器类型，武器名称长度相同
            //所以这几个映射足够覆盖当前版本所有武器
            builder.Append(character switch
            {
                '鉄' or '鐵' => '铁',
                '黒' => '黑',
                '劍' or '剣' => '剑',
                '蝕' => '蚀',
                '鍾' or '鐘' => '钟',
                '銀' => '银',
                '彈' or '弾' => '弹',
                '獵' or '猟' => '猎',
                _ => character
            });
        }

        return builder.ToString();
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

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadWeaponNames()
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
