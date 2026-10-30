using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>某个物品的一组底部提示；操作使用展示时固定的事件集合，不借用最高优先级灯的目标。</summary>
public sealed class AccountRecordsNoticeCard : ObservableObject
{
    /// <summary>创建卡片时固定的账号。</summary>
    public string Uid { get; init; } = "";
    /// <summary>物品名称。</summary>
    public string Name { get; init; } = "";
    /// <summary>合并状态。</summary>
    public string State { get; init; } = "";
    /// <summary>卡片状态色与库存灯保持一致。</summary>
    public string AccentColor => State switch { "Error" => "#EA4335", "Warning" => "#F28C28", "Pending" => "#FBBC05", "Completed" => "#34A853", _ => "#9AA0A6" };
    /// <summary>非错误、非提醒卡片的事件类型。</summary>
    public string Kind { get; init; } = "";
    /// <summary>本张卡片所包含的事件 ID。</summary>
    public string[] EventIds { get; init; } = [];
    /// <summary>卡片展示时各事件的内容版本，防止确认后台更新后的未知内容。</summary>
    public IReadOnlyDictionary<string, string> EventVersions { get; init; } = new Dictionary<string, string>();
    /// <summary>关联当前库存错误；舍弃操作保留其并发校验。</summary>
    public string? ErrorId { get; init; }
    /// <summary>卡片标题。</summary>
    public string Title { get; init; } = "";
    /// <summary>同组说明去重合并，保留不同错误或提醒的可读文本。</summary>
    public string Message { get; init; } = "";
    /// <summary>来源、变动量及完成批次等摘要。</summary>
    public string Details { get; init; } = "";
    /// <summary>当前库存错误使用舍弃入口。</summary>
    public bool CanResolve => State == "Error" && ErrorId != null;
    /// <summary>待核实不能归档；完成卡片仍遵守服务层归档资格。</summary>
    public bool CanArchive { get; init; }
    /// <summary>本账号中符合既有同类完成归档规则的事件 ID。</summary>
    public string[] BatchArchiveIds { get; init; } = [];
    /// <summary>至少两个同类完成事件时提供批量入口。</summary>
    public bool CanBatchArchive => CanArchive && State == "Completed" && BatchArchiveIds.Length >= 2;

    /// <summary>沿用状态灯优先级；Error、Warning 分别合并，其他状态按事件类型合并。</summary>
    public static AccountRecordsNoticeCard[] Build(InventoryWorkspace workspace, string name)
    {
        var data = workspace.Inventory;
        var events = data.Events.Where(item => item.Name == name && !item.Archived && !item.ItemDeleted).ToArray();
        data.Errors.TryGetValue(name, out var error);
        // 复用灯的排序选择，避免卡片排序与表格最高优先级不一致。
        var ordered = events.OrderBy(InventoryEventPolicy.Priority).ThenByDescending(item => item.Id, StringComparer.Ordinal);
        return ordered.GroupBy(item => (item.State, Kind: item.State is "Error" or "Warning" ? "" : item.Kind))
            .Select(group =>
            {
                var items = group.ToArray();
                var first = items[0];
                var candidates = first.State == "Completed"
                    ? InventoryEventPolicy.GetCompletedArchiveCandidates(data, first.Kind).Select(item => item.Id).ToArray() : [];
                string SourceName(string source) => workspace.Settings.PreferReadableSource && workspace.Sources.TryGetValue(source, out var descriptor)
                    ? descriptor.DisplayName : source;
                var label = first.State switch
                {
                    "Error" => "错误", "Warning" => "提醒", "Completed" => "已完成",
                    "Pending" when first.Kind == "ExpirationCheck" => "过期批次待核实",
                    "Pending" when first.Kind == "Delta" => "增减待核实", "Pending" => "待核实", _ => "其他事项"
                };
                var details = "来源：" + string.Join("、", items.Select(item => SourceName(item.Source)).Distinct());
                if (first.Kind == "Delta" && first.State is not ("Error" or "Warning"))
                    details += $"；累计变动：{items.Sum(item => (decimal)item.Quantity):+0;-0;±0}";
                if (first.State == "Completed" && first.Kind == "Delta")
                    details += $"；包含 {items.Select(item => item.VerificationId).Distinct().Count()} 轮核实，详见事件记录";
                return new AccountRecordsNoticeCard
                {
                    Uid = data.Uid, Name = name, State = first.State, Kind = group.Key.Kind,
                    EventIds = items.Select(item => item.Id).ToArray(), ErrorId = first.State == "Error" ? error?.Id : null,
                    EventVersions = items.ToDictionary(item => item.Id, item => item.ContentVersion),
                    Title = $"{name} · {label}（{items.Length} 条）",
                    Message = string.Join("\n", items.Select(item => AccountRecordsErrorMessages.ForDisplay(item.Message)).Distinct()),
                    Details = details,
                    CanArchive = first.State == "Warning" || first.State == "Error" && error == null ||
                        first.State == "Completed" && items.All(item => candidates.Contains(item.Id)),
                    BatchArchiveIds = candidates
                };
            }).ToArray();
    }
}
