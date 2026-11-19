using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>将库存快照转换为展示行，不读写存储或管理界面选中状态。</summary>
internal sealed class AccountRecordsInventoryRowFactory(InventoryCatalog catalog, InventoryIconCache icons)
{
    /// <summary>在后台准备行快照，避免事件分组占用 UI 线程。</summary>
    public Task<AccountRecordsInventoryRow[]> BuildAsync(InventoryWorkspace workspace) => Task.Run(() =>
    {
        // 每轮只扫描一次事件，建行时直接访问当前材料的分组。
        var eventsByName = workspace.Inventory.Events.Where(item => !item.Archived).ToLookup(item => item.Name, StringComparer.Ordinal);
        var rows = workspace.Inventory.Items.Keys.Union(workspace.Inventory.Errors.Keys, StringComparer.Ordinal)
        .Union(eventsByName.Select(group => group.Key), StringComparer.Ordinal).Select(name =>
        {
            workspace.Inventory.Items.TryGetValue(name, out var recent);
            workspace.Inventory.Errors.TryGetValue(name, out var error);
            var current = recent?.FirstOrDefault();
            var reference = current?.Source ?? error?.Source ?? "";
            workspace.Sources.TryGetValue(reference, out var source);
            workspace.Definitions.TryGetValue(name, out var category);
            var events = eventsByName[name].ToArray();
            var active = InventoryEventPolicy.SelectActive(events);
            var pending = events.Where(item => item.Kind == "Delta" && item.State == "Pending").ToArray();
            var changes = active?.Kind == "Delta" ? events.Where(item => item.Kind == "Delta" && item.State == active.State &&
                (active.State != "Completed" || item.VerificationId == active.VerificationId)).ToArray() : [];
            // 历史文件可能显式保存空来源；展示时按缺失来源处理，不让单个事件阻断整表。
            string Display(string? key) => key != null && workspace.Sources.TryGetValue(key, out var descriptor) && workspace.Settings.PreferReadableSource ? descriptor.DisplayName : key ?? "";
            var origins = changes.Select(item => Display(item.Source)).Distinct().ToArray();
            var firstOrigin = origins.FirstOrDefault() ?? Display(active?.Source ?? reference);
            if (firstOrigin.Length > 24) firstOrigin = firstOrigin[..24] + "…";
            var changeTotal = changes.Sum(item => (decimal)item.Quantity).ToString("+0;-0;±0");
            var details = changes.Length > 0
                ? origins.Length > 1
                    ? string.Format(I18nService.Instance.Translate("变动来源：{0}，共 {1} 处，累计变动：{2}"), firstOrigin, origins.Length, changeTotal)
                    : string.Format(I18nService.Instance.Translate("变动来源：{0}，累计变动：{1}"), firstOrigin, changeTotal)
                : string.Format(I18nService.Instance.Translate("核实来源：{0}"), firstOrigin);
            if (active is { State: "Completed", Kind: "Delta" })
            {
                var verified = active.VerifiedCorrect == true ? I18nService.Instance.Translate("一致")
                    : active.VerifiedCorrect == false ? I18nService.Instance.Translate("不一致") : I18nService.Instance.Translate("未记录比对结果");
                details += string.Format(I18nService.Instance.Translate("，累计推算与确认库存：{0}，核实来源：{1}"), verified, Display(active.VerificationSource));
            }
            if (active?.Kind == "CategoryWarning")
                details = string.Format(I18nService.Instance.Translate("上报来源：{0}，分类采用本地值"), Display(active.Source));
            if (active?.Kind == "ExpirationCheck")
                details = I18nService.Instance.Translate("已过期库存待检查；请重新上报到期批次或手动修订数量。");
            if (active?.Kind == "ThresholdWarning")
                details = I18nService.Instance.Translate("满足自定义规则；归档后本轮条件解除前不再提醒。");
            return new AccountRecordsInventoryRow
            {
                LastScanAttemptId = recent?.FirstOrDefault(record => !string.IsNullOrEmpty(record.AttemptId))?.AttemptId
                    ?? events.LastOrDefault(item => !string.IsNullOrEmpty(item.AttemptId))?.AttemptId,
                Event = active, PendingDelta = pending.Length > 0 ? pending.Sum(item => (decimal)item.Quantity) : null, StatusDetails = details,
                ErrorCount = events.Count(item => item.State == "Error"),
                Name = name, ItemNameSortKey = catalog.GetItemNameSortKey(name), Prior = workspace.Inventory.Prior.Contains(name),
                Category = string.IsNullOrEmpty(category) ? I18nService.Key("未分类") : category,
                Quantity = current?.Quantity, RecordedAt = current?.RecordedAt,
                // 无总库存时仍展示增量中已知的到期批次；空记录仅用于批次重放，不作为确认数量。
                Expirations = InventoryStore.EstimateExpirations(current ?? new InventoryRecord(), pending),
                ExpirationsEstimated = pending.Length > 0,
                SourceReference = reference,
                SourceName = workspace.Settings.PreferReadableSource && !string.IsNullOrWhiteSpace(source?.DisplayName) ? source.DisplayName : reference,
                IconLoader = workspace.Settings.UseImageCache ? () => icons.Load(name) : null, Error = error, Recent = recent ?? []
            };
        }).ToArray();
        // 批量入口与服务端使用同一资格规则；需求等未来类型不会与核实绿灯混合。
        var counts = rows.Where(row => row.Event?.State == "Completed").Select(row => row.Event!.Kind).Distinct()
            .ToDictionary(kind => kind, kind => InventoryEventPolicy.GetCompletedArchiveCandidates(workspace.Inventory, kind).Length);
        foreach (var row in rows)
            row.CanBatchArchive = row.StateRank == 3 && row.Event != null && counts.GetValueOrDefault(row.Event.Kind) >= 2;
        return rows;
    });


}
