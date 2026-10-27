using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>库存列表的一行，由持久化快照生成，不直接绑定可写业务数据。</summary>
public sealed partial class AccountRecordsInventoryRow : ObservableObject
{
    /// <summary>当前行固定的排序键；不持久化到库存文件。</summary>
    private AccountRecordsSortValues? _sortValues;
    /// <summary>首次加入表格时取快照，此后只在用户主动排序时更新。</summary>
    public AccountRecordsSortValues SortValues => _sortValues ??= new(this);
    /// <summary>用户主动排序时读取最新内容；由调用方统一刷新视图。</summary>
    public void CaptureSortValues() => _sortValues = new(this);
    /// <summary>应用新的展示数据，保留行身份、选中状态、图像和既定排序键。</summary>
    public void ApplySnapshot(AccountRecordsInventoryRow next, bool refreshIcon = false)
    {
        // 必须在修改内容前固定排序键，避免尚未参与比较的行在刷新时改变位置。
        _ = SortValues;
        var resetIcon = refreshIcon || (IconLoader == null) != (next.IconLoader == null);
        Prior = next.Prior;
        LastScanAttemptId = next.LastScanAttemptId;
        Event = next.Event;
        ErrorCount = next.ErrorCount;
        CanBatchArchive = next.CanBatchArchive;
        PendingDelta = next.PendingDelta;
        StatusDetails = next.StatusDetails;
        Name = next.Name;
        ItemNameSortKey = next.ItemNameSortKey;
        Category = next.Category;
        Quantity = next.Quantity;
        RecordedAt = next.RecordedAt;
        Expirations = next.Expirations;
        ExpirationsEstimated = next.ExpirationsEstimated;
        _earliestExpiration = null;
        SourceReference = next.SourceReference;
        SourceName = next.SourceName;
        Error = next.Error;
        Recent = next.Recent;
        IconLoader = next.IconLoader;
        if (resetIcon)
        {
            _iconVersion++;
            _icon = null;
            _iconRequested = false;
        }
        // 展示绑定统一更新，排序绑定仍读取固定快照。
        OnPropertyChanged(string.Empty);
    }
    /// <summary>当前账号是否将此物品置顶；始终优先于用户选择的列排序。</summary>
    public bool Prior { get; set; }
    /// <summary>右键菜单根据当前置顶状态显示操作名称。</summary>
    public string PriorActionText => Prior ? "取消置顶" : "置顶";
    /// <summary>仅在可见置顶区与普通区之间显示分隔线。</summary>
    [ObservableProperty] private bool _showPriorSeparator;
    /// <summary>最高优先级的未归档事件。</summary>
    public InventoryEvent? Event { get; set; }
    /// <summary>该材料未归档的错误事件数量；重复接收次数不算新的错误。</summary>
    public int ErrorCount { get; set; }
    /// <summary>当前底部提示正在展示本行事件。</summary>
    [ObservableProperty] private bool _isNoticeActive;
    /// <summary>最近一次可追溯扫描批次，包括失败上报。</summary>
    public string? LastScanAttemptId { get; set; }
    /// <summary>状态优先级：红、橘、黄、绿、灰、无灯。</summary>
    public int StateRank => HasError ? 0 : Event?.State switch { "Error" => 0, "Warning" => 1, "Pending" => 2, "Completed" => 3, "Planned" => 4, _ => 5 };
    /// <summary>是否显示状态灯。</summary>
    public bool HasStatusLight => StateRank < 5;
    /// <summary>当前物品为未受阻的绿灯，且账号内同类合法归档目标至少有两条。</summary>
    [ObservableProperty] private bool _canBatchArchive;
    /// <summary>只能接受当前实际展示且对应待处理库存冲突的错误，不能借最新增量错误接受旧记录。</summary>
    public bool CanAcceptDisplayedError => Error?.CanAccept == true && Event is { Kind: "Report", State: "Error", Archived: false } &&
        Event.ErrorId == Error.Id;
    /// <summary>累计待核实增量；为空表示没有待核实变动。</summary>
    public decimal? PendingDelta { get; set; }
    /// <summary>附在确认库存右侧，不参与主数量居中。</summary>
    public string DeltaText => PendingDelta is { } value ? $"（{(value > 0 ? "+" : value == 0 ? "±" : "")}{value}）" : "";
    /// <summary>两行提示的来源及变动摘要。</summary>
    public string StatusDetails { get; set; } = "";
    /// <summary>悬停同样显示两行摘要。</summary>
    public string StatusText => (Event?.Message ?? Error?.Message ?? "") + "\n" + StatusDetails;
    /// <summary>标准名称或待修订名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>由物品字典生成的名称排序文本，不用于展示或存储关联。</summary>
    public string ItemNameSortKey { get; set; } = "";
    /// <summary>展示分类。</summary>
    public string Category { get; set; } = "";
    /// <summary>当前有效数量，未知保持空值。</summary>
    public long? Quantity { get; set; }
    /// <summary>用于时间排序的 UTC 时间。</summary>
    public DateTimeOffset? RecordedAt { get; set; }
    /// <summary>按当前系统时区展示时间。</summary>
    public string TimeText => RecordedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未记录";
    /// <summary>到期批次是否包含尚未核实的增量推算。</summary>
    public bool ExpirationsEstimated { get; set; }
    /// <summary>当前分批到期信息；存在待核实增量时展示推算结果。</summary>
    public IReadOnlyList<InventoryExpiration>? Expirations { get; set; }
    /// <summary>按当前展示批次的最早到期时间排序，不使用剩余天数字符串。</summary>
    public DateTimeOffset? EarliestExpiration => _earliestExpiration ??= Expirations is { Count: > 0 } ? Expirations.Min(item => item.Time) : null;
    /// <summary>行快照的到期排序键，避免排序比较时反复解析全部时间戳。</summary>
    private DateTimeOffset? _earliestExpiration;
    /// <summary>到期排序时，有到期信息的材料始终排在无信息材料之前。</summary>
    public bool HasExpiration => Expirations is { Count: > 0 };
    /// <summary>表格显示剩余天数，多批同时标注数量。</summary>
    public string ExpirationText => Expirations is not { Count: > 0 } ? ExpirationsEstimated && Expirations != null ? "无剩余批次（推算）" : "—" : string.Join("；", Expirations.Select(item =>
        $"{item.Quantity} 个：{Remaining(item.Time)}")) + (ExpirationsEstimated ? "（推算）" : "");
    /// <summary>悬停逐行显示各批次数量和剩余天数。</summary>
    public string ExpirationDetails => Expirations is not { Count: > 0 } ? ExpirationsEstimated && Expirations != null ? "已知到期批次推算已消耗完，等待完整库存核实。" : "未提供过期信息" : string.Join("\n", Expirations.Select(item =>
        $"{item.Quantity} 个：{Remaining(item.Time)}")) + (ExpirationsEstimated ? "\n包含待核实增量，按最早到期优先推算消耗。" : "");
    /// <summary>按实际时长显示完整天数，不足一天独立提示。</summary>
    private static string Remaining(DateTimeOffset time)
    {
        var remaining = time - DateTimeOffset.UtcNow;
        return remaining <= TimeSpan.Zero ? "已过期" : remaining.TotalDays < 1 ? "不足 1 天" : $"{Math.Floor(remaining.TotalDays)} 天";
    }
    /// <summary>刷新时间与事件展示，不重建行或触发图像重新加载。</summary>
    public void RefreshTimeAndStatus()
    {
        OnPropertyChanged(nameof(ExpirationText));
        OnPropertyChanged(nameof(ExpirationDetails));
        OnPropertyChanged(nameof(Event));
        OnPropertyChanged(nameof(StateRank));
        OnPropertyChanged(nameof(HasStatusLight));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusDetails));
    }
    /// <summary>可读名称或原始目录。</summary>
    public string SourceName { get; set; } = "";
    /// <summary>完整来源引用。</summary>
    public string SourceReference { get; set; } = "";
    /// <summary>仅在可见单元格或详情绑定图标时调用；关闭图像缓存时为空。</summary>
    public Func<ImageSource?>? IconLoader { get; set; }
    /// <summary>本行首次加载的图像，随行模型释放；不保证滚出视口后立即释放。</summary>
    private ImageSource? _icon;
    /// <summary>同一行只尝试加载一次，失败后通过刷新列表创建新行重试。</summary>
    private bool _iconRequested;
    /// <summary>忽略清理或替换图像之前启动的旧加载结果。</summary>
    private int _iconVersion;
    /// <summary>首次使用时后台加载，读取列表数据不解码图片。</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                if (IconLoader != null) _ = LoadIconAsync();
            }
            return _icon;
        }
    }

    /// <summary>加载失败保持占位；在绑定发起的 UI 上下文通知图片就绪。</summary>
    private async Task LoadIconAsync()
    {
        var version = _iconVersion;
        var loader = IconLoader;
        ImageSource? image;
        try { image = loader == null ? null : await Task.Run(loader); }
        catch (Exception) { image = null; }
        if (version != _iconVersion) return;
        _icon = image;
        OnPropertyChanged(nameof(Icon));
    }
    /// <summary>当前待处理异常。</summary>
    public InventoryError? Error { get; set; }
    /// <summary>只有错误才显示红灯，不混入需求状态。</summary>
    public bool HasError => Error != null;
    /// <summary>悬停说明包含下一步操作提示。</summary>
    public string ErrorText => Error == null ? "" : $"{Error.Message}\n请单击查看详情，或右键选择“修改数量 / 修订字典”手动订正。";
    /// <summary>有界最近来源记录。</summary>
    public IReadOnlyList<InventoryRecord> Recent { get; set; } = [];
}
