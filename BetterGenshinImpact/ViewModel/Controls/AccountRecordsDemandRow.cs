using System;
using System.Globalization;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.Service.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>需求行复用物品展示信息，库存错误优先展示，需求进度独立保留。</summary>
public partial class AccountRecordsDemandRow : ObservableObject
{
    /// <summary>库存物品的名称、分类、数量和惰性图标。</summary>
    public required AccountRecordsInventoryRow Item { get; init; }
    /// <summary>当前需求；为空时保留物品行但不显示灯。</summary>
    public MaterialDemand? Demand { get; set; }
    /// <summary>根据库存证据计算的需求进度。</summary>
    public MaterialDemandProgress? Progress { get; set; }
    /// <summary>目标登记来源的可读名称，与库存及完成证据的来源独立。</summary>
    public string Source { get; set; } = "";
    /// <summary>原位应用需求证据，保留表格选择、图标及提示卡片所引用的行。</summary>
    public void ApplySnapshot(AccountRecordsDemandRow next, bool refreshIcon = false)
    {
        Item.ApplySnapshot(next.Item, refreshIcon);
        Demand = next.Demand;
        Progress = next.Progress;
        Source = next.Source;
        OnPropertyChanged(string.Empty);
    }
    /// <summary>是否有需求，在玩家置顶之后作为分组依据。</summary>
    public bool HasDemand => Demand != null;
    /// <summary>需求页自己的置顶标记，由需求文件赋值给本页独立的展示行。</summary>
    public bool Prior => Item.Prior;
    /// <summary>沿用库存的置顶菜单文案。</summary>
    public string PriorActionText => Item.PriorActionText;
    /// <summary>需求灯状态；无需求为空。</summary>
    public MaterialDemandState? State => Progress?.State;
    /// <summary>有需求且库存显示红灯时，优先引导处理库存错误。</summary>
    public bool HasInventoryError => HasDemand && Item.StateRank == 0;
    /// <summary>库存红灯覆盖需求灯，其余库存提醒不改变需求灯。</summary>
    public int StateRank => HasInventoryError ? 0 : State switch { MaterialDemandState.Pending => 2, MaterialDemandState.Completed => 3, MaterialDemandState.Unmet => 4, _ => 5 };
    /// <summary>底部提示正在展示本行时沿用库存的高亮反馈。</summary>
    [ObservableProperty] private bool _isNoticeActive;
    /// <summary>仅玩家置顶与普通物品之间显示分隔线。</summary>
    [ObservableProperty] private bool _showPriorSeparator;
    /// <summary>状态文案；无需求与未达标分开表达。</summary>
    public string StatusText => I18nService.Instance.Translate(HasInventoryError
        ? "库存存在错误，请先前往库存记录处理" : Progress?.Message ?? "未设置需求");
    /// <summary>提示卡片沿用库存状态的强调色。</summary>
    public string NoticeAccentColor => HasInventoryError ? "#EA4335" : State switch
    {
        MaterialDemandState.Pending => "#FBBC05",
        MaterialDemandState.Completed => "#34A853",
        _ => "#9AA0A6"
    };
    /// <summary>卡片详情展示当前数量、目标及来源。</summary>
    public string NoticeDetails => (HasInventoryError ? (Item.Error?.Message ?? Item.Event?.Message ?? "") + "\n" : "")
        + $"{I18nService.Instance.Translate("库存")}：{QuantityText}    {I18nService.Instance.Translate("目标")}：{TargetQuantity}"
        + (string.IsNullOrEmpty(Source) ? "" : $"\n{I18nService.Instance.Translate("目标来源")}：{Source}")
        + (Progress?.ConfirmationMethod is "Family" or "FamilyDelta" or "FamilyEstimate"
            ? $"\n{I18nService.Instance.Translate("同族材料折算数量")}：{Progress.EstimatedQuantity}（{I18nService.Instance.Translate("非实际库存")}）"
                + (Progress.CraftingReservePercent > 0
                    ? $"\n{I18nService.Instance.Translate("合成收益预留")}：{Progress.CraftingReservePercent}%" : "")
            : "");
    /// <summary>目标库存数量。</summary>
    public long? TargetQuantity => Demand?.TargetQuantity;
    /// <summary>无直接需求时目标列留空。</summary>
    public string TargetQuantityText => TargetQuantity?.ToString(CultureInfo.InvariantCulture) ?? "";
    /// <summary>目标创建时间，修改目标时重新开始。</summary>
    public DateTimeOffset? CreatedAt => Demand?.CreatedAt;
    /// <summary>目标登记时间的完整提示，未创建目标时留空。</summary>
    public string CreatedAtText => CreatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "";
    /// <summary>当前确认数量与待核实增量的统一显示。</summary>
    public string QuantityText => Item.QuantityText + Item.AdditionalDeltaText;
    /// <summary>分类排序优先使用背包页顺序，再比较材料类型。</summary>
    public string CategorySortKey => $"{InventoryCatalog.GetPageSortOrder(Item.Page):D4}\0{Item.Page}\0{Item.MaterialType}";
    /// <summary>是否允许人工确认未完成目标。</summary>
    public bool CanConfirm => HasDemand && !HasInventoryError && State != MaterialDemandState.Completed;
    /// <summary>只有当前已完成需求可以归档。</summary>
    public bool CanArchive => HasDemand && !HasInventoryError && State == MaterialDemandState.Completed;
    /// <summary>可见物品行号，随筛选及排序更新。</summary>
    [ObservableProperty] private string _displayRowNumber = "";
}
