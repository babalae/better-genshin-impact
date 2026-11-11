using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 状态文件和不可变历史文件共用的一次完整运行记录。
/// </summary>
public sealed class PuloniaTaskRunRecord
{
    /// <summary>
    /// 执行实例键（<c>primary</c>、<c>child_session</c>、<c>webview:&lt;小写实例名&gt;</c>）：提交时取执行本运行的实例，续跑取发起续跑的实例。
    /// 本运行的状态与历史只由该实例写入；执行记录据此标明运行在哪个实例。
    /// </summary>
    [JsonProperty("instance", Required = Required.Always)]
    public string Instance { get; set; } = PuloniaInstanceKey.Primary;

    /// <summary>指定账户的运行在执行前完整核验的实际 UID，续跑时按它再次核验；跟随当前账户的运行为空。</summary>
    [JsonProperty("verified_uid")]
    public string? VerifiedUid { get; set; }

    /// <summary>
    /// 运行账户 ID，含游戏节点的运行在执行第一个节点前确定；不含游戏节点或尚未开始时为空。
    /// 续跑时先沿用原运行的值，开始时按续跑规则核对。路线 CD 判定和完成记录、首领领奖次数都按它读写。
    /// </summary>
    [JsonProperty("account_id")]
    public string? AccountId { get; set; }

    /// <summary>运行账户的来源：实例当前账户或计划指定；未确定运行账户时为空。</summary>
    [JsonProperty("account_source")]
    [JsonConverter(typeof(StringEnumConverter), typeof(SnakeCaseNamingStrategy))]
    public PuloniaTaskAccountSource? AccountSource { get; set; }

    /// <summary>跟随当前账户的运行开始时的 UID 比对结果；指定账户或未确定运行账户时为空。</summary>
    [JsonProperty("uid_check")]
    [JsonConverter(typeof(StringEnumConverter), typeof(SnakeCaseNamingStrategy))]
    public PuloniaTaskUidCheck? UidCheck { get; set; }

    /// <summary>UID 比对不一致时游戏中读到的 UID；其他情况为空。</summary>
    [JsonProperty("observed_uid")]
    public string? ObservedUid { get; set; }

    /// <summary>
    /// 运行 ID，形如 <c>run_yyyyMMdd-HHmmss-xxxx</c>；提交、查询、取消、历史文件名和完成记录的幂等键都使用此身份。
    /// </summary>
    [JsonProperty("run_id", Required = Required.Always)]
    public string RunId { get; set; } = string.Empty;

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
    /// 本次完成的路线：每份地图追踪路线整份成功后，先写入运行账户的完成记录，再随节点结果写入这里；
    /// 与完成记录同一结构，保存每次成功的明细。没有时为空集合，不凭执行状态补造。
    /// </summary>
    [JsonProperty("route_completions", Required = Required.Always)]
    public List<PuloniaRouteCompletion> RouteCompletions { get; set; } = [];

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
    public string? ResumedFromRunId { get; set; }

    /// <summary>
    /// 复制一份只替换快照 JSON 的浅副本；请求与各集合沿用同一实例，双方此后都不能再修改。
    /// 运行历史常驻内存时用它去掉快照正文，快照按需从历史文件读取。
    /// </summary>
    /// <param name="snapshotJson">副本使用的快照 JSON；常驻历史传空字符串。</param>
    /// <returns>新的记录对象。</returns>
    internal PuloniaTaskRunRecord WithSnapshotJson(string snapshotJson) => new()
    {
        Instance = Instance,
        VerifiedUid = VerifiedUid,
        AccountId = AccountId,
        AccountSource = AccountSource,
        UidCheck = UidCheck,
        ObservedUid = ObservedUid,
        RunId = RunId,
        Request = Request,
        PlanName = PlanName,
        SnapshotJson = snapshotJson,
        Status = Status,
        CurrentTaskAddress = CurrentTaskAddress,
        Message = Message,
        SubmittedAt = SubmittedAt,
        StartedAt = StartedAt,
        FinishedAt = FinishedAt,
        NodeResults = NodeResults,
        RouteCompletions = RouteCompletions,
        CompletedTaskAddresses = CompletedTaskAddresses,
        ResumeFromTaskAddress = ResumeFromTaskAddress,
        ResumedFromRunId = ResumedFromRunId
    };
}
