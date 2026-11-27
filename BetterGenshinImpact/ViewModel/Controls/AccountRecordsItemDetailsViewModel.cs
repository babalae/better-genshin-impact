using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using System.Linq;
using BetterGenshinImpact.Service.AccountRecords.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>某来源最近一次确认的库存详情。</summary>
public sealed record AccountRecordsSourceRecord(string Label, long Quantity, string RecordedAt, string SourceName, string Source, string? ExpirationDetails)
{
    /// <summary>可选过期信息仅在有实际批次内容时显示，未提供时不占据详情空间。</summary>
    public bool HasExpirationDetails => !string.IsNullOrEmpty(ExpirationDetails);
}

/// <summary>历史摘要和可展开的原始证据。</summary>
public sealed record AccountRecordsHistoryRecord(string Summary, string Evidence);

/// <summary>物品当前确认库存与保留范围内的批次历史。</summary>
public sealed partial class AccountRecordsItemDetailsViewModel : ObservableObject
{
    /// <summary>物品名称、分类和图标。</summary>
    public AccountRecordsInventoryRow Item { get; }
    /// <summary>与库存存储一致，当前记录排在第一项。</summary>
    public IReadOnlyList<AccountRecordsSourceRecord> Records { get; }
    /// <summary>没有有效记录时的说明。</summary>
    public bool IsEmpty => Records.Count == 0;

    /// <summary>沿用来源名称显示偏好，同时保留实际标识以便溯源。</summary>
    public AccountRecordsItemDetailsViewModel(AccountRecordsInventoryRow item, IReadOnlyDictionary<string, AccountRecordSource> sources, bool readable,
        Func<Task<InventoryBatchIndexEntry[]>>? loadHistory = null,
        Func<IReadOnlyList<InventoryRecordReference>, Task<InventoryBatchRecord?[]>>? loadPage = null)
    {
        Item = item;
        _loadHistory = loadHistory;
        _loadPage = loadPage;
        HasMore = loadHistory != null && loadPage != null;
        // 当前库存单独展示，其他来源观测统一从批次历史查询。
        Records = item.Recent.Take(1)
            .Select(record => new AccountRecordsSourceRecord(
            "当前库存", record.Quantity,
            record.RecordedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            readable && sources.TryGetValue(record.Source, out var source) && !string.IsNullOrWhiteSpace(source.DisplayName) ? source.DisplayName : record.Source,
            record.Source, record.Expirations is not { Count: > 0 } ? null : string.Join("\n", record.Expirations.Select(batch =>
                $"{batch.Quantity} 个：{batch.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}")))).ToArray();
    }

    /// <summary>读取打开详情时的历史索引快照。</summary>
    private readonly Func<Task<InventoryBatchIndexEntry[]>>? _loadHistory;
    /// <summary>整个查看会话固定的引用顺序；不受后续重试、新增记录影响。</summary>
    private InventoryBatchIndexEntry[]? _historySnapshot;
    /// <summary>打开后被清理的证据数量，不能声称已全部展示。</summary>
    private int _missingRecords;
    /// <summary>服务层按引用顺序一次返回整页证据，界面不再逐批查找记录。</summary>
    private readonly Func<IReadOnlyList<InventoryRecordReference>, Task<InventoryBatchRecord?[]>>? _loadPage;
    /// <summary>已读取的索引条数，与实际可显示的证据数量分开。</summary>
    private int _offset;
    /// <summary>避免分页期间新增记录造成重复卡片。</summary>
    private readonly HashSet<InventoryRecordReference> _shown = [];
    /// <summary>已加载的历史卡片。</summary>
    public ObservableCollection<AccountRecordsHistoryRecord> History { get; } = [];
    /// <summary>是否还有可加载的历史。</summary>
    [ObservableProperty] private bool _hasMore;
    /// <summary>历史加载或空数据说明。</summary>
    [ObservableProperty] private string _historyStatus = "";

    /// <summary>每页读取 50 条，同一页内每个批次只读取一次；失败时保留重试入口。</summary>
    [RelayCommand]
    private async Task LoadMore()
    {
        if (!HasMore || _loadHistory == null || _loadPage == null) return;
        try
        {
            HistoryStatus = "正在读取历史…";
            _historySnapshot ??= await _loadHistory();
            var entries = _historySnapshot.Skip(_offset).Take(50).ToArray();
            var records = await _loadPage(entries.Select(entry => entry.Reference).ToArray());

            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                var record = records[index];
                if (record == null) { _missingRecords++; continue; }
                if (!_shown.Add(entry.Reference)) continue;
                var kind = record.Kind switch { "Report" => "总量上报", "Delta" => "增量上报", "Manual" => "人工修订", "Failure" => "采集失败", _ => record.Kind };
                var outcome = record.Outcome switch { "Accepted" => "已接收", "Rejected" => "已拒绝", "Duplicate" => "重复记录", _ => record.Outcome };
                var quantity = record.Input.GetValue(record.Kind == "Delta" ? "Delta" : "Quantity", StringComparison.OrdinalIgnoreCase);
                var quantityText = quantity == null ? "" : $"\n{(record.Kind == "Delta" ? "变动数量" : "上报数量")}：{quantity}";
                History.Add(new($"{kind} · {outcome}\n处理时间：{record.ProcessedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n来源：{record.Source}\n{record.Message}{quantityText}", $"批次 / 记录：{entry.Reference}\n原始记录：\n{record.Input.ToString(Formatting.Indented)}"));
            }
            _offset += entries.Length;
            HasMore = _offset < _historySnapshot.Length;
            HistoryStatus = HasMore ? "已加载部分历史，可继续加载。" : _missingRecords > 0
                ? $"读取完成；其中 {_missingRecords} 条记录无法读取，可能已删除，请检查批次文件。"
                : History.Count == 0 ? "暂无批次历史。"
                : "已展示本次打开时的全部历史；重新打开可查看新增记录。";
        }
        catch (Exception ex)
        {
            HistoryStatus = $"历史读取失败，可重试：{ex.Message}";
        }
    }
}
