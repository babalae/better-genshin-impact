using BetterGenshinImpact.Service.AccountRecords;
using System;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BetterGenshinImpact.Service.AccountRecords.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>当前账号保留范围内的事件集合，包括未归档与已归档记录。</summary>
public partial class AccountRecordsEventsViewModel : ObservableObject
{
    /// <summary>完整快照；筛选只影响显示，不删除存储记录。</summary>
    private List<InventoryEvent> _allEvents = [];
    /// <summary>完整账号快照；列表筛选不能隐藏影响归档资格的错误或待处理事件。</summary>
    private InventoryDocument _inventory;
    /// <summary>由页面固定账号后提供批次读取入口，界面不直接访问历史文件。</summary>
    private readonly Func<string, Task<InventoryBatch?>>? _loadBatch;
    /// <summary>按需加载的所属批次内容，不影响当前事件筛选和状态灯。</summary>
    [ObservableProperty] private string _batchDetails = "";
    /// <summary>选中事件具有可查询批次时提供查看入口。</summary>
    public bool HasBatch => _loadBatch != null && Selected?.RecordReference != null;
    /// <summary>切换事件后清除先前批次详情。</summary>
    partial void OnSelectedChanged(InventoryEvent? value)
    {
        BatchDetails = "";
        OnPropertyChanged(nameof(HasBatch));
    }
    /// <summary>显式加载所属批次，异步完成时确认用户仍选中原事件。</summary>
    [RelayCommand]
    private async Task ShowBatch()
    {
        if (_loadBatch == null || Selected?.RecordReference is not { } reference) return;
        var selectedId = Selected.Id;
        try
        {
            var batch = await _loadBatch(reference.BatchId);
            if (Selected?.Id != selectedId) return;
            BatchDetails = batch == null ? "该批次历史已不在保留范围内。" :
                $"批次：{batch.BatchId}\n类型：{batch.Kind}    原有 {batch.OriginalCount} 条，已清理 {batch.RemovedCount} 条\n" +
                string.Join("\n\n", batch.Records.Select(item => $"{item.RecordId} · {item.Name} · {item.Outcome}\n{item.Message}\n{item.Input}"));
        }
        catch (Exception ex)
        {
            if (Selected?.Id == selectedId) BatchDetails = AccountRecordsErrorMessages.Describe(ex);
        }
    }
    /// <summary>默认优先排查错误，关闭后查看全部事件。</summary>
    [ObservableProperty] private bool _errorsOnly = true;
    /// <summary>不区分大小写的关键词，空白表示不限制。</summary>
    [ObservableProperty] private string _searchText = "";
    /// <summary>关键词变化时重新筛选，并尽量保留当前选中项。</summary>
    partial void OnSearchTextChanged(string value) => RefreshEvents(Selected?.Id);
    /// <summary>匹配名称、来源、说明、事件标识及原始追溯内容。</summary>
    private static bool MatchesSearch(InventoryEvent item, string keyword) => keyword.Length == 0 ||
        item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        item.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        item.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        (item.Message).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        item.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        item.AttemptId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        item.RecordReference?.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase) == true ||
        item.VerificationReference?.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase) == true ||
        item.VerificationSource.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        item.History.Any(line => line.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    /// <summary>切换筛选时尽量保留当前定位。</summary>
    partial void OnErrorsOnlyChanged(bool value) => RefreshEvents(Selected?.Id);
    /// <summary>按筛选重建显示集合，并同步批量操作入口。</summary>
    private void RefreshEvents(string? id)
    {
        var keyword = SearchText.Trim();
        // 一次替换显示集合，避免每次输入时逐条触发列表刷新。
        Events = new(_allEvents.Where(item => (!ErrorsOnly || item.State == "Error") && MatchesSearch(item, keyword)).Reverse());
        OnPropertyChanged(nameof(Events));
        Selected = Events.FirstOrDefault(item => item.Id == id) ?? Events.FirstOrDefault();
        OnPropertyChanged(nameof(HasCompletedEvents));
    }
    /// <summary>由调用者固定账号的归档与重载操作。</summary>
    private readonly Func<string, IReadOnlyCollection<string>, Task<InventoryDocument>> _archiveCompleted;
    /// <summary>按当前选中完成事件确定类型，仅计入筛选列表内未受阻的同类目标。</summary>
    private InventoryEvent[] CompletedCandidates()
    {
        if (Selected is not { State: "Completed", Archived: false } selected) return [];
        var visible = Events.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var eligible = InventoryEventPolicy.GetCompletedArchiveCandidates(_inventory, selected.Kind);
        if (!eligible.Any(item => item.Id == selected.Id)) return [];
        return eligible.Where(item => visible.Contains(item.Id)).ToArray();
    }
    /// <summary>当前类型及筛选范围内至少两条合法目标时提供入口。</summary>
    public bool HasCompletedEvents => CompletedCandidates().Length >= 2;
    /// <summary>归档后重载列表，保留当前事件定位。</summary>
    [RelayCommand]
    private async Task ArchiveCompletedEvents()
    {
        var candidates = CompletedCandidates();
        if (candidates.Length < 2 || Selected == null) return;
        var kind = Selected.Kind;
        try
        {
            var id = Selected?.Id;
            _inventory = await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _archiveCompleted(kind, candidates.Select(item => item.Id).ToArray()));
            _allEvents = _inventory.Events.ToList();
            RefreshEvents(id);
        }
        catch (Exception ex)
        {
            await ThemedMessageBox.ShowAsync(AccountRecordsErrorMessages.Describe(ex), "事件记录", MessageBoxButton.OK, ThemedMessageBox.MessageBoxIcon.Error);
        }
    }
    /// <summary>按创建顺序倒序展示。</summary>
    public ObservableCollection<InventoryEvent> Events { get; private set; } = [];
    /// <summary>选中事件。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Details))] [NotifyPropertyChangedFor(nameof(HistoryDetails))]
    [NotifyPropertyChangedFor(nameof(HasCompletedEvents))] private InventoryEvent? _selected;
    /// <summary>主要说明与有效字段，原始证据独立展示。</summary>
    public string Details
    {
        get
        {
            if (Selected is not { } item) return "请选择事件。";
            var status = item.State == "Aborted" ? "已终止（由人工确认库存替代）" : item.State;
            var lines = new List<string>
            {
                $"事件 ID：{item.Id}",
                $"批次记录：{item.RecordReference?.ToString() ?? "系统检查事件"}",
                (item.Message),
                $"状态：{status}    {(item.Archived ? "已归档" : "未归档")}",
                "",
                $"物品：{item.Name}",
                $"起止时间：{item.TimeRange}",
                $"来源：{item.Source}"
            };
            if (item.VerificationReference != null) lines.Add($"核实依据：{item.VerificationReference}");
            if (!string.IsNullOrWhiteSpace(item.VerificationSource)) lines.Add($"核实来源：{item.VerificationSource}");
            lines.Add($"记录时间：{item.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            lines.Add($"{(item.Kind == "Delta" ? "增减数量" : "上报数量")}：{item.Quantity}");
            if (item.Baseline is { } baseline) lines.Add($"原库存：{baseline}");
            return string.Join("\n", lines);
        }
    }
    /// <summary>保留未经改写的完整历史，供溯源及复制。</summary>
    public string HistoryDetails => Selected == null ? "" : $"RepeatCount={Selected.RepeatCount}\nLastReceivedAt={Selected.LastReceivedAt:O}\n\n" + string.Join("\n\n", Selected.History);
    /// <summary>由调用者提供快照，可按初始关键词筛选并定位到指定事件。</summary>
    public AccountRecordsEventsViewModel(InventoryDocument inventory, string? id, Func<string, IReadOnlyCollection<string>, Task<InventoryDocument>> archiveCompleted, string? searchText = null,
        Func<string, Task<InventoryBatch?>>? loadBatch = null)
    {
        _loadBatch = loadBatch;
        _archiveCompleted = archiveCompleted;
        _inventory = inventory;
        _allEvents = inventory.Events.ToList();
        _searchText = searchText ?? "";
        // 从具体提醒跳转时优先展示目标，避免默认过滤隐藏 Pending 或 Warning。
        if (id != null && _allEvents.Any(item => item.Id == id && item.State != "Error")) _errorsOnly = false;
        RefreshEvents(id);
    }
}
