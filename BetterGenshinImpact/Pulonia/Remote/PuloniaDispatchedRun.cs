using System;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service.Instance;

namespace BetterGenshinImpact.Pulonia.Remote;

/// <summary>
/// 主实例在本次进程中下发（或经子实例补发得知）的一个运行的显示状态，只在内存中，不落盘；
/// 运行状态与历史由执行它的子实例保存。对象不可变，状态变化时整体替换。
/// </summary>
public sealed record PuloniaDispatchedRun
{
    /// <summary>子实例生成的运行 ID。</summary>
    public required string RunId { get; init; }

    /// <summary>计划 ID。</summary>
    public required string PlanId { get; init; }

    /// <summary>计划名称；子实例尚未上报时为空。</summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary>执行实例最近一次登记的端点；子实例重连后由它的上报更新。</summary>
    public required InstanceEndpoint Endpoint { get; init; }

    /// <summary>执行实例键（<c>child_session</c>、<c>webview:&lt;小写实例名&gt;</c>）。</summary>
    public required string Instance { get; init; }

    /// <summary>执行实例的显示名，例如“云原神 · A”。</summary>
    public required string InstanceName { get; init; }

    /// <summary>子实例最近一次上报的运行状态。</summary>
    public PuloniaTaskRunStatus Status { get; init; }

    /// <summary>子实例最近一次上报的当前节点消息或最终原因。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>提交时间。</summary>
    public DateTimeOffset SubmittedAt { get; init; }

    /// <summary>开始时间；尚未开始时为空。</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>结束时间；尚未结束时为空。</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>
    /// 与执行实例的连接已断开、此后还没有收到它的上报：状态未知，不能远程停止，需要在子实例界面停止。
    /// </summary>
    public bool IsDisconnected { get; init; }

    /// <summary>最近一次上报的状态是否仍未结束（排队、运行中或停止中）。</summary>
    public bool IsActive => PuloniaTaskRunClassifier.IsActive(Status);

    /// <summary>主实例能否远程停止：连接正常且运行未结束。</summary>
    public bool CanCancel => !IsDisconnected && IsActive;
}
