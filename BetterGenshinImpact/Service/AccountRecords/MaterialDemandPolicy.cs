using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>需求进度只消费库存证据，不读取库存状态灯或修改库存事件。</summary>
public static partial class MaterialDemandPolicy
{
    /// <summary>按当前库存和待核实增量判断达标，不追踪需求创建后的执行经过。</summary>
    public static MaterialDemandProgress Evaluate(MaterialDemand demand, InventoryRecord? current,
        IEnumerable<InventoryEvent> events, bool useDelta = false)
    {
        var pending = events.Where(item => !item.ItemDeleted && !item.Archived &&
            item.Kind == "Delta" && item.State == "Pending").ToArray();
        decimal? estimate = current == null ? null : current.Quantity + pending.Sum(item => (decimal)item.Quantity);
        if (demand.ConfirmedAt != null)
            return new(MaterialDemandState.Completed, "人工确认达标", current?.Source ?? "", estimate, demand.ConfirmedAt, "Manual");
        // 已有实测库存也可满足新目标；未核实增量存在时不把旧总量当作最终结果。
        if (current != null && current.Quantity >= demand.TargetQuantity && pending.Length == 0)
            return new(MaterialDemandState.Completed, "实测数量已达标", current.Source, estimate, current.RecordedAt, "Quantity");
        if (pending.Length > 0 && estimate >= demand.TargetQuantity)
        {
            var latest = pending.MaxBy(item => item.RecordedAt)!;
            return useDelta
                ? new(MaterialDemandState.Completed, "增量推算已达标", latest.Source, estimate, latest.RecordedAt, "Delta")
                : new(MaterialDemandState.Pending, "可能达标，待核实", latest.Source, estimate);
        }
        return new(MaterialDemandState.Unmet, "未达标", current?.Source ?? "", estimate);
    }
}

/// <summary>需求状态：无需求由空需求表达，不创建库存灯的错误或提醒状态。</summary>
public enum MaterialDemandState
{
    /// <summary>存在需求，当前数量未达标或缺少可判断的库存基准。</summary>
    Unmet,
    /// <summary>待核实增量推算可能达标，尚未确认完成。</summary>
    Pending,
    /// <summary>人工、已有或后续实测总量、已启用的增量推算确认达标。</summary>
    Completed
}

/// <summary>纯计算的需求进度和库存来源。</summary>
/// <param name="State">需求灯状态。</param>
/// <param name="Message">供界面翻译的状态文案。</param>
/// <param name="Source">当前参考的库存或达标增量的来源。</param>
/// <param name="EstimatedQuantity">含待核实增量的推算总量；未知基准保持空。</param>
/// <param name="CompletedAt">当前完成证据的时间。</param>
/// <param name="ConfirmationMethod">稳定的确认方式标识，不依赖界面翻译。</param>
/// <param name="CraftingReservePercent">确认依赖随机合成收益时实际使用的预留率；基础合成或单项判断为零。</param>
public sealed record MaterialDemandProgress(MaterialDemandState State, string Message, string Source, decimal? EstimatedQuantity,
    DateTimeOffset? CompletedAt = null, string? ConfirmationMethod = null, int CraftingReservePercent = 0);
