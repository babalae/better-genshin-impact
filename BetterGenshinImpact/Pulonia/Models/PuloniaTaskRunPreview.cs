using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 运行预览中一个叶子节点的预测结果。
/// </summary>
public enum PuloniaTaskPreviewOutcome
{
    /// <summary>预计执行。</summary>
    Execute,

    /// <summary>节点自身或祖先已关闭。</summary>
    Disabled,

    /// <summary>路线仍在 CD 中，跳过。</summary>
    CoolingDown,

    /// <summary>一次性路线已成功执行，跳过。</summary>
    Completed,

    /// <summary>与本次前面的节点是同一文件，前一项成功后跳过。</summary>
    RepeatedInRun
}

/// <summary>
/// 运行预览中的一个叶子节点。
/// </summary>
public sealed class PuloniaTaskRunPreviewItem
{
    /// <summary>快照中的节点地址。</summary>
    public required string TaskAddress { get; init; }

    /// <summary>节点名称。</summary>
    public required string Name { get; init; }

    /// <summary>任务类型。</summary>
    public required string TaskType { get; init; }

    /// <summary>预测结果。</summary>
    public required PuloniaTaskPreviewOutcome Outcome { get; init; }

    /// <summary>地图追踪节点固定的路线 CD 规则；其他类型为空。</summary>
    public PuloniaCollectionCooldownPolicy? Policy { get; init; }

    /// <summary>地图追踪节点按当前时间的 CD 状态；其他类型为空。</summary>
    public PuloniaCollectionCooldownStatus? Cooldown { get; init; }

    /// <summary>预测为本次重复时，前面同一文件的节点名称。</summary>
    public string? RepeatedTaskName { get; init; }
}

/// <summary>
/// 按计划当前配置对一次运行的逐项预测；只读，不入队、不写运行状态。
/// </summary>
/// <param name="PlanName">计划名称。</param>
/// <param name="ScopeKey">本次预测使用的路线 CD 作用域。</param>
/// <param name="PreparedAt">预测时刻。</param>
/// <param name="Items">按执行顺序排列的叶子节点。</param>
public sealed record PuloniaTaskRunPreview(string PlanName, string ScopeKey, DateTimeOffset PreparedAt,
    IReadOnlyList<PuloniaTaskRunPreviewItem> Items)
{
    /// <summary>预计执行的节点数。</summary>
    public int ExecuteCount => Items.Count(item => item.Outcome == PuloniaTaskPreviewOutcome.Execute);

    /// <summary>预计因路线 CD 跳过的节点数，含本次重复。</summary>
    public int CooldownSkipCount => Items.Count(item => item.Outcome is PuloniaTaskPreviewOutcome.CoolingDown
        or PuloniaTaskPreviewOutcome.Completed or PuloniaTaskPreviewOutcome.RepeatedInRun);

    /// <summary>预计执行、但刷新规则未识别的地图追踪节点数。</summary>
    public int UnmatchedCount => Items.Count(item => item.Outcome == PuloniaTaskPreviewOutcome.Execute
        && item.Cooldown?.State == PuloniaCollectionCooldownState.Unmatched);
}
