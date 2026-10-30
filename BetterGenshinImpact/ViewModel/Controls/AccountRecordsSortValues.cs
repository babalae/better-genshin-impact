using System;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>用户最近一次排序时的展示键；行内容变化不会改写这些键。</summary>
public sealed class AccountRecordsSortValues(AccountRecordsInventoryRow row)
{
    /// <summary>置顶分组。</summary>
    public bool Prior { get; private set; } = row.Prior;
    /// <summary>显式置顶只改变分组，其余排序键仍保持上次表头排序的值。</summary>
    public void UpdatePrior(bool prior) => Prior = prior;
    /// <summary>名称排序键。</summary>
    public string ItemNameSortKey { get; } = row.ItemNameSortKey;
    /// <summary>分类排序键。</summary>
    public string Category { get; } = row.Category;
    /// <summary>数量排序键。</summary>
    public long? Quantity { get; } = row.Quantity;
    /// <summary>记录时间排序键。</summary>
    public DateTimeOffset? RecordedAt { get; } = row.RecordedAt;
    /// <summary>来源排序键。</summary>
    public string SourceName { get; } = row.SourceName;
    /// <summary>状态排序键。</summary>
    public int StateRank { get; } = row.StateRank;
    /// <summary>有状态灯分组。</summary>
    public bool HasStatusLight { get; } = row.HasStatusLight;
    /// <summary>有到期信息分组。</summary>
    public bool HasExpiration { get; } = row.HasExpiration;
    /// <summary>最早到期时间排序键。</summary>
    public DateTimeOffset? EarliestExpiration { get; } = row.EarliestExpiration;
}
