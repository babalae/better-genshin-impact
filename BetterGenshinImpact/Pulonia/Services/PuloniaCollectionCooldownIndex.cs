using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 账本按（作用域，资源键）建立的只读索引，供界面和运行预览多次查询；由一份已提交账本构建，账本变化后重建。
/// </summary>
public sealed class PuloniaCollectionCooldownIndex
{
    /// <summary>作用域 → 资源键 → 最新记录。</summary>
    private readonly Dictionary<string, Dictionary<string, PuloniaTaskLedgerEntry>> _byScope = new(StringComparer.Ordinal);

    /// <summary>没有任何记录的索引。</summary>
    public static PuloniaCollectionCooldownIndex Empty { get; } = new([]);

    /// <summary>建立索引；同一身份有多条时保留最晚的一条。</summary>
    /// <param name="ledger">已提交账本。</param>
    public PuloniaCollectionCooldownIndex(IEnumerable<PuloniaTaskLedgerEntry> ledger)
    {
        foreach (var entry in ledger)
        {
            if (!_byScope.TryGetValue(entry.ScopeKey, out var entries))
                _byScope[entry.ScopeKey] = entries = new Dictionary<string, PuloniaTaskLedgerEntry>(StringComparer.Ordinal);
            if (!entries.TryGetValue(entry.EffectKey, out var current) || current.OccurredAt < entry.OccurredAt)
                entries[entry.EffectKey] = entry;
        }
    }

    /// <summary>出现过记录的作用域。</summary>
    public IReadOnlyCollection<string> Scopes => _byScope.Keys;

    /// <summary>全部最新记录。</summary>
    public IEnumerable<PuloniaTaskLedgerEntry> Entries => _byScope.Values.SelectMany(entries => entries.Values);

    /// <summary>查找（作用域，文件）的最新记录。</summary>
    public PuloniaTaskLedgerEntry? Find(string scopeKey, string effectKey)
        => _byScope.TryGetValue(scopeKey, out var entries) && entries.TryGetValue(effectKey, out var entry) ? entry : null;

    /// <summary>
    /// 枚举某个作用域下资源键以指定前缀开头的记录，供目录引用在不展开目录时计数。
    /// </summary>
    /// <param name="scopeKey">作用域键。</param>
    /// <param name="effectKeyPrefix">目录对应的资源键前缀，以 <c>/</c> 结尾。</param>
    /// <param name="recursive">是否包含子目录中的文件。</param>
    public IEnumerable<PuloniaTaskLedgerEntry> EnumerateUnder(string scopeKey, string effectKeyPrefix, bool recursive)
    {
        if (!_byScope.TryGetValue(scopeKey, out var entries))
            yield break;
        foreach (var (key, entry) in entries)
        {
            if (!key.StartsWith(effectKeyPrefix, StringComparison.Ordinal))
                continue;
            if (!recursive && key.IndexOf('/', effectKeyPrefix.Length) >= 0)
                continue;
            yield return entry;
        }
    }
}
