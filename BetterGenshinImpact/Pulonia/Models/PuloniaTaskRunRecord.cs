using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 状态文件和不可变历史文件共用的一次完整运行记录。
/// </summary>
public sealed class PuloniaTaskRunRecord
{
    /// <summary>
    /// 请求 ID。
    /// </summary>
    [JsonProperty("request_id", Required = Required.Always)]
    public Guid RequestId { get; set; }

    /// <summary>
    /// 运行 ID。
    /// </summary>
    [JsonProperty("run_id", Required = Required.Always)]
    public Guid RunId { get; set; }

    /// <summary>
    /// 固定后的调用请求。
    /// </summary>
    [JsonProperty("request", Required = Required.Always)]
    public PuloniaTaskRequest Request { get; set; } = new();

    /// <summary>
    /// 提交时的计划名称。
    /// </summary>
    [JsonProperty("plan_name", Required = Required.Always)]
    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// 提交时固定的完整快照 JSON。
    /// </summary>
    [JsonProperty("snapshot_json", Required = Required.Always)]
    public string SnapshotJson { get; set; } = string.Empty;

    /// <summary>
    /// 当前或最终运行状态。
    /// </summary>
    [JsonProperty("status", Required = Required.Always)]
    public PuloniaTaskRunStatus Status { get; set; }

    /// <summary>
    /// 当前节点地址。
    /// </summary>
    [JsonProperty("current_task_address")]
    public string? CurrentTaskAddress { get; set; }

    /// <summary>
    /// 状态摘要。
    /// </summary>
    [JsonProperty("message", Required = Required.Always)]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 提交时间。
    /// </summary>
    [JsonProperty("submitted_at", Required = Required.Always)]
    public DateTimeOffset SubmittedAt { get; set; }

    /// <summary>
    /// 开始时间。
    /// </summary>
    [JsonProperty("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// 结束时间。
    /// </summary>
    [JsonProperty("finished_at")]
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// 节点尝试结果。
    /// </summary>
    [JsonProperty("node_results", Required = Required.DisallowNull)]
    public List<PuloniaTaskNodeResult> NodeResults { get; set; } = [];

    /// <summary>
    /// 已确认的副作用；旧历史未提供此字段时保持空集合，不凭执行状态补造事件。
    /// </summary>
    [JsonProperty("confirmed_effects", Required = Required.DisallowNull)]
    public List<PuloniaTaskConfirmedEffect> ConfirmedEffects { get; set; } = [];

    /// <summary>
    /// 本次运行执行前已经确认完成、因续跑而跳过的地址。
    /// </summary>
    [JsonProperty("completed_task_addresses", Required = Required.DisallowNull)]
    public List<string> CompletedTaskAddresses { get; set; } = [];

    /// <summary>
    /// 显式从此节点开始的地址；为空表示继续未完成节点。
    /// </summary>
    [JsonProperty("resume_from_task_address")]
    public string? ResumeFromTaskAddress { get; set; }

    /// <summary>
    /// 来源运行 ID；普通新运行为空。
    /// </summary>
    [JsonProperty("resumed_from_run_id")]
    public Guid? ResumedFromRunId { get; set; }

    /// <summary>
    /// 执行前已落盘、但尚未由确认事件结清的操作意图。
    /// </summary>
    [JsonProperty("operation_intent")]
    public PuloniaTaskOperationIntent? OperationIntent { get; set; }
}
