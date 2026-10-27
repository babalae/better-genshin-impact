using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.AccountRecords.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>某来源最近一次确认的库存详情。</summary>
public sealed record AccountRecordsSourceRecord(string Label, long Quantity, string RecordedAt, string SourceName, string Source);

/// <summary>物品资料与最近来源记录的只读快照，不作为完整变动历史。</summary>
public sealed class AccountRecordsItemDetailsViewModel : ObservableObject
{
    /// <summary>物品名称、分类和图标。</summary>
    public AccountRecordsInventoryRow Item { get; }
    /// <summary>与库存存储一致，当前记录排在第一项。</summary>
    public IReadOnlyList<AccountRecordsSourceRecord> Records { get; }
    /// <summary>没有有效记录时的说明。</summary>
    public bool IsEmpty => Records.Count == 0;

    /// <summary>沿用来源名称显示偏好，同时保留实际标识以便溯源。</summary>
    public AccountRecordsItemDetailsViewModel(AccountRecordsInventoryRow item, IReadOnlyDictionary<string, AccountRecordSource> sources, bool readable)
    {
        Item = item;
        Records = item.Recent.Select((record, index) => new AccountRecordsSourceRecord(
            index == 0 ? "当前库存" : "最近来源记录", record.Quantity,
            record.RecordedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            readable && sources.TryGetValue(record.Source, out var source) && !string.IsNullOrWhiteSpace(source.DisplayName) ? source.DisplayName : record.Source,
            record.Source)).ToArray();
    }
}