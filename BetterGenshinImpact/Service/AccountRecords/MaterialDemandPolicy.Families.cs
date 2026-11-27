using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Material;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>统一计算整张需求表，家族分配同时考虑所有等级目标，展示与归档使用相同结果。</summary>
public static partial class MaterialDemandPolicy
{
    /// <summary>先计算单项证据，再通过独立材料目录按低到高保留所有活动目标并折算。</summary>
    public static Dictionary<string, MaterialDemandProgress> EvaluateAll(InventoryWorkspace workspace, MaterialDemandDocument demands)
    {
        var events = workspace.Inventory.Events.ToLookup(item => item.Name);
        var result = demands.Items.ToDictionary(pair => pair.Key, pair => Evaluate(pair.Value,
            workspace.Inventory.Items.GetValueOrDefault(pair.Key)?.FirstOrDefault(), events[pair.Key], workspace.Settings.UseDeltaForDemands));
        if (!workspace.Settings.UseFamiliesForDemands) return result;
        // 有错误的库存不能成为另一项需求的合成依据，避免跨物品绕过库存红灯。
        var invalidStock = workspace.Inventory.Errors.Keys.Concat(workspace.Inventory.Events
            .Where(item => !item.ItemDeleted && !item.Archived && item.State == "Error")
            .Select(item => item.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var family in MaterialFamilyCatalog.Families)
        {
            var levels = family.Members;
            if (!levels.Any(demands.Items.ContainsKey)) continue;
            var current = levels.Select(name => workspace.Inventory.Items.GetValueOrDefault(name)?.FirstOrDefault()).ToArray();
            var pending = levels.Select(name => events[name].Where(item => !item.ItemDeleted && !item.Archived &&
                item.Kind == "Delta" && item.State == "Pending").ToArray()).ToArray();
            // 缺少基准时不把增量变成有效库存。已知基准的负增量也必须扣除，不能沿用过时总量。
            var stock = current.Select((item, index) => item == null ? 0 :
                (decimal)item.Quantity + pending[index].Sum(entry => (decimal)entry.Quantity)).ToArray();
            var usable = levels.Select((name, index) => current[index] != null &&
                !invalidStock.Contains(name) && stock[index] >= 0).ToArray();
            // 未知、错误或推算为负的材料不提供库存，其目标仍保留，其他可靠成员可独立参与合成。
            var inputs = levels.Select((name, index) => (
                Stock: usable[index] ? stock[index] : 0,
                Required: (decimal)(demands.Items.GetValueOrDefault(name)?.TargetQuantity ?? 0))).ToArray();
            var reserve = family.AllowsCraftingBonus
                ? Math.Clamp(workspace.Settings.DemandCraftingReservePercent, 0, MaterialFamilyCalculator.MaxCraftingBonusReservePercent)
                : 0;
            var basic = MaterialFamilyCalculator.CalculateAvailable(inputs, 0);
            var estimated = reserve == 0 ? basic : MaterialFamilyCalculator.CalculateAvailable(inputs, reserve);
            InventoryRecord? evidence = null;
            var relevantPending = new List<InventoryEvent>();
            for (var index = 0; index < levels.Count; index++)
            {
                // 低阶没有产生可传递材料时，后续合成重新由本阶库存提供依据。
                if (estimated[index] == inputs[index].Stock)
                {
                    evidence = null;
                    relevantPending.Clear();
                }
                // 升级只依赖当前及更低阶；无基准或更高阶增量不影响本项的完成状态和证据时间。
                if (usable[index])
                {
                    if (evidence == null || current[index]!.RecordedAt > evidence.RecordedAt) evidence = current[index];
                    relevantPending.AddRange(pending[index]);
                }
                var name = levels[index];
                if (evidence == null || !demands.Items.TryGetValue(name, out var target) || result[name].State == MaterialDemandState.Completed ||
                    estimated[index] < target.TargetQuantity) continue;
                var usesBonus = basic[index] < target.TargetQuantity;
                var hasPending = relevantPending.Count > 0;
                var confirmed = !hasPending || workspace.Settings.UseDeltaForDemands;
                // 合成数量只作为达成依据，不生成实际库存；不同依据不混记为实测确认。
                var message = !confirmed ? "同族材料数量可能达标，待核实"
                    : usesBonus ? "同族材料数量估算已达标（含合成收益预留）"
                    : hasPending ? "同族材料增量推算已达标" : "同族材料数量可合成达标";
                var completedAt = !hasPending ? evidence.RecordedAt
                    : relevantPending.Select(item => item.RecordedAt).Append(evidence.RecordedAt).Max();
                result[name] = new MaterialDemandProgress(confirmed ? MaterialDemandState.Completed : MaterialDemandState.Pending,
                    message, evidence.Source, estimated[index], confirmed ? completedAt : null,
                    !confirmed ? null : usesBonus ? "FamilyEstimate" : hasPending ? "FamilyDelta" : "Family", usesBonus ? reserve : 0);
            }
        }
        return result;
    }
}
