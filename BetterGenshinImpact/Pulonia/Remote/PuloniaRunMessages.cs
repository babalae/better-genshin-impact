using System;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BetterGenshinImpact.Pulonia.Remote;

// pulonia.run.* 的消息模型：经实例通信的 Newtonsoft 序列化器（驼峰属性名、省略空值）传输。
// 负载只有计划 ID、修订号与显示所需字段，不携带快照；快照由子实例读取共享计划后自己固定。

/// <summary>
/// <c>pulonia.run.submit</c>（主 → 子）：请求子实例运行一个已保存的计划。
/// </summary>
public sealed class PuloniaRunSubmitRequest
{
    /// <summary>要运行的计划 ID。</summary>
    public string PlanId { get; init; } = string.Empty;

    /// <summary>主实例保存后的计划修订号；子实例读到的修订号不同时拒绝。</summary>
    public long PlanRevision { get; init; }

    /// <summary>执行范围：只运行该节点及其子树；为空表示整个计划。</summary>
    public string? TargetTaskId { get; init; }
}

/// <summary>
/// <c>pulonia.run.submit</c> 的响应：子实例已把运行放入本实例队列。
/// </summary>
public sealed class PuloniaRunSubmitResponse
{
    /// <summary>子实例生成的运行 ID。</summary>
    public string RunId { get; init; } = string.Empty;
}

/// <summary>
/// <c>pulonia.run.cancel</c>（主 → 子）：请求子实例停止一个由主实例下发的运行；子实例只发出取消，不等待运行结束。
/// </summary>
public sealed class PuloniaRunCancelRequest
{
    /// <summary>要停止的运行 ID。</summary>
    public string RunId { get; init; } = string.Empty;
}

/// <summary>
/// <c>pulonia.run.changed</c>（子 → 主）：主实例下发的运行的最新状态，只用于主实例显示。
/// </summary>
public sealed class PuloniaRunChangedReport
{
    /// <summary>运行 ID。</summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>计划 ID。</summary>
    public string PlanId { get; init; } = string.Empty;

    /// <summary>提交时的计划名称。</summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary>运行状态，按名称传输，枚举增删值时不会错位。</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public PuloniaTaskRunStatus Status { get; init; }

    /// <summary>当前节点消息或最终原因。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>当前节点地址；未执行或已结束时为空。</summary>
    public string? CurrentTaskAddress { get; init; }

    /// <summary>提交时间。</summary>
    public DateTimeOffset SubmittedAt { get; init; }

    /// <summary>开始时间；尚未开始时为空。</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>结束时间；尚未结束时为空。</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>
    /// 由子实例的运行视图生成上报。
    /// </summary>
    /// <param name="run">运行视图</param>
    /// <returns>上报内容</returns>
    public static PuloniaRunChangedReport FromView(PuloniaTaskRunView run) => new()
    {
        RunId = run.RunId,
        PlanId = run.PlanId,
        PlanName = run.PlanName,
        Status = run.Status,
        Message = run.Message,
        CurrentTaskAddress = run.CurrentTaskAddress,
        SubmittedAt = run.SubmittedAt,
        StartedAt = run.StartedAt,
        FinishedAt = run.FinishedAt
    };
}
