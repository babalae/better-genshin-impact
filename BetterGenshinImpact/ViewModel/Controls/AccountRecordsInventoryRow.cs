using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using BetterGenshinImpact.Service.AccountRecords.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>库存列表的一行，由持久化快照生成，不直接绑定可写业务数据。</summary>
public sealed partial class AccountRecordsInventoryRow : ObservableObject
{
    /// <summary>最高优先级的未归档事件。</summary>
    public InventoryEvent? Event { get; init; }
    /// <summary>当前底部提示正在展示本行事件。</summary>
    [ObservableProperty] private bool _isNoticeActive;
    /// <summary>最近一次可追溯扫描批次，包括失败上报。</summary>
    public string? LastScanAttemptId { get; init; }
    /// <summary>状态优先级：红、橘、黄、绿、灰、无灯。</summary>
    public int StateRank => HasError ? 0 : Event?.State switch { "Error" => 0, "Warning" => 1, "Pending" => 2, "Completed" => 3, "Planned" => 4, _ => 5 };
    /// <summary>是否显示状态灯。</summary>
    public bool HasStatusLight => StateRank < 5;
    /// <summary>累计待核实增量；为空表示没有待核实变动。</summary>
    public decimal? PendingDelta { get; init; }
    /// <summary>附在确认库存右侧，不参与主数量居中。</summary>
    public string DeltaText => PendingDelta is { } value ? $"（{(value > 0 ? "+" : value == 0 ? "±" : "")}{value}）" : "";
    /// <summary>两行提示的来源及变动摘要。</summary>
    public string StatusDetails { get; init; } = "";
    /// <summary>悬停同样显示两行摘要。</summary>
    public string StatusText => (Event?.Message ?? Error?.Message ?? "") + "\n" + StatusDetails;
    /// <summary>标准名称或待修订名称。</summary>
    public string Name { get; init; } = "";
    /// <summary>展示分类。</summary>
    public string Category { get; init; } = "";
    /// <summary>当前有效数量，未知保持空值。</summary>
    public long? Quantity { get; init; }
    /// <summary>用于时间排序的 UTC 时间。</summary>
    public DateTimeOffset? RecordedAt { get; init; }
    /// <summary>按当前系统时区展示时间。</summary>
    public string TimeText => RecordedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未记录";
    /// <summary>可读名称或原始目录。</summary>
    public string SourceName { get; init; } = "";
    /// <summary>完整来源引用。</summary>
    public string SourceReference { get; init; } = "";
    /// <summary>冻住的共享图标。</summary>
    public ImageSource? Icon { get; init; }
    /// <summary>当前待处理异常。</summary>
    public InventoryError? Error { get; init; }
    /// <summary>只有错误才显示红灯，不混入需求状态。</summary>
    public bool HasError => Error != null;
    /// <summary>悬停说明包含下一步操作提示。</summary>
    public string ErrorText => Error == null ? "" : $"{Error.Message}\n请单击查看详情，或右键选择“修改数量 / 修订字典”手动订正。";
    /// <summary>有界最近来源记录。</summary>
    public IReadOnlyList<InventoryRecord> Recent { get; init; } = [];
}
