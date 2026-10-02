using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 提供给页面和进程内调用方的不可变运行状态视图。
/// </summary>
public sealed class PuloniaTaskRunView
{
    /// <summary>
    /// 提交操作生成的请求 ID。
    /// </summary>
    public Guid RequestId { get; }

    /// <summary>
    /// 本次运行 ID；步骤 6 的历史和续跑将使用此身份关联。
    /// </summary>
    public Guid RunId { get; }

    /// <summary>
    /// 入口计划 ID。
    /// </summary>
    public string PlanId { get; }

    /// <summary>
    /// 入口计划名称，取自提交时固定的计划 JSON。
    /// </summary>
    public string PlanName { get; }

    /// <summary>
    /// 调用来源短文本。
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// 当前运行状态。
    /// </summary>
    public PuloniaTaskRunStatus Status { get; }

    /// <summary>
    /// 当前正在执行或最近结束的节点地址。
    /// </summary>
    public string? CurrentTaskAddress { get; }

    /// <summary>
    /// 当前状态摘要或最终原因。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 请求提交并完成快照固定的时间，使用 UTC。
    /// </summary>
    public DateTimeOffset SubmittedAt { get; }

    /// <summary>
    /// 真正从串行队列开始执行的时间，尚未开始时为 null。
    /// </summary>
    public DateTimeOffset? StartedAt { get; }

    /// <summary>
    /// 确认执行器退出后的完成时间，未完成时为 null。
    /// </summary>
    public DateTimeOffset? FinishedAt { get; }

    /// <summary>
    /// 提交时固定的完整运行快照 JSON，用于验证编辑隔离。
    /// </summary>
    public string SnapshotJson { get; }

    /// <summary>
    /// 已结束的节点尝试结果。
    /// </summary>
    public IReadOnlyList<PuloniaTaskNodeResult> NodeResults { get; }

    /// <summary>
    /// 建立运行状态的防御性副本。
    /// </summary>
    internal PuloniaTaskRunView(Guid requestId, Guid runId, string planId, string planName, string source,
        PuloniaTaskRunStatus status, string? currentTaskAddress, string message, DateTimeOffset submittedAt,
        DateTimeOffset? startedAt, DateTimeOffset? finishedAt, string snapshotJson,
        IEnumerable<PuloniaTaskNodeResult> nodeResults)
    {
        RequestId = requestId;
        RunId = runId;
        PlanId = planId;
        PlanName = planName;
        Source = source;
        Status = status;
        CurrentTaskAddress = currentTaskAddress;
        Message = message;
        SubmittedAt = submittedAt;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        SnapshotJson = snapshotJson;
        NodeResults = new ReadOnlyCollection<PuloniaTaskNodeResult>(nodeResults.ToArray());
    }
}
