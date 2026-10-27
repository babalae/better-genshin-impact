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

/// <summary>当前账号的完整亮灯事件集合，包括归档记录。</summary>
public partial class AccountRecordsEventsViewModel : ObservableObject
{
    /// <summary>由调用者固定账号的归档与重载操作。</summary>
    private readonly Func<Task<IEnumerable<InventoryEvent>>> _archiveCompleted;
    /// <summary>当前列表是否有已完成且未归档的事件。</summary>
    public bool HasCompletedEvents => Events.Count(item => item.State == "Completed" && !item.Archived) >= 2;
    /// <summary>归档后重载列表，保留当前事件定位。</summary>
    [RelayCommand]
    private async Task ArchiveCompletedEvents()
    {
        try
        {
            var id = Selected?.Id;
            var events = await _archiveCompleted();
            Events.Clear();
            foreach (var item in events.Reverse()) Events.Add(item);
            Selected = Events.FirstOrDefault(item => item.Id == id) ?? Events.FirstOrDefault();
            OnPropertyChanged(nameof(HasCompletedEvents));
        }
        catch (Exception ex)
        {
            await ThemedMessageBox.ShowAsync(ex.Message, "事件记录", MessageBoxButton.OK, ThemedMessageBox.MessageBoxIcon.Error);
        }
    }
    /// <summary>按创建顺序倒序展示。</summary>
    public ObservableCollection<InventoryEvent> Events { get; }
    /// <summary>选中事件。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Details))] [NotifyPropertyChangedFor(nameof(HistoryDetails))] private InventoryEvent? _selected;
    /// <summary>主要说明与有效字段，原始证据独立展示。</summary>
    public string Details
    {
        get
        {
            if (Selected is not { } item) return "请选择事件。";
            var status = item.State;
            var lines = new List<string>
            {
                $"事件 ID：{item.Id}",
                item.Message,
                $"状态：{status}    {(item.Archived ? "已归档" : "未归档")}",
                "",
                $"物品：{item.Name}",
                $"起止时间：{item.TimeRange}",
                $"来源：{item.Source}"
            };
            if (!string.IsNullOrWhiteSpace(item.VerificationSource)) lines.Add($"核实来源：{item.VerificationSource}");
            lines.Add($"记录时间：{item.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            lines.Add($"{(item.Kind == "Delta" ? "增减数量" : "上报数量")}：{item.Quantity}");
            if (item.Baseline is { } baseline) lines.Add($"原库存：{baseline}");
            return string.Join("\n", lines);
        }
    }
    /// <summary>保留未经改写的完整历史，供溯源及复制。</summary>
    public string HistoryDetails => Selected == null ? "" : $"RepeatCount={Selected.RepeatCount}\nLastReceivedAt={Selected.LastReceivedAt:O}\n\n" + string.Join("\n\n", Selected.History);
    /// <summary>由调用者提供快照，可定位到指定事件。</summary>
    public AccountRecordsEventsViewModel(IEnumerable<InventoryEvent> events, string? id, Func<Task<IEnumerable<InventoryEvent>>> archiveCompleted)
    {
        _archiveCompleted = archiveCompleted;
        Events = new(events.Reverse());
        Selected = Events.FirstOrDefault(item => item.Id == id) ?? Events.FirstOrDefault();
    }
}
