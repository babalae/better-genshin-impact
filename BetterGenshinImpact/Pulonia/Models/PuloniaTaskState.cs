using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// Pulonia 的唯一当前运行状态文件模型。
/// </summary>
public sealed class PuloniaTaskState
{
    /// <summary>
    /// 当前支持的状态格式版本。4 起所有执行实例共用一个串行队列，活动运行只有一个，
    /// 并去掉通用额度框架的未决操作，触发游标只保存防重复字段；开发期不升级旧版本文件。
    /// </summary>
    public const int CurrentSchemaVersion = 4;

    /// <summary>
    /// 状态格式版本。
    /// </summary>
    [JsonProperty("schema_version", Required = Required.Always)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 每次成功保存递增的状态序列。
    /// </summary>
    [JsonProperty("sequence", Required = Required.Always)]
    public long Sequence { get; set; }

    /// <summary>
    /// 已固定快照、尚未开始的请求。
    /// </summary>
    [JsonProperty("pending_requests", Required = Required.DisallowNull)]
    public List<PuloniaTaskRunRecord> PendingRequests { get; set; } = [];

    /// <summary>
    /// 唯一活动运行；在主实例或子实例执行都占用同一个串行队列，<see cref="PuloniaTaskRequest.ExecutionTarget"/> 记录执行实例。
    /// </summary>
    [JsonProperty("active_run")]
    public PuloniaTaskRunRecord? ActiveRun { get; set; }

    /// <summary>
    /// 已结束但尚未确认写入不可变历史文件的运行。
    /// </summary>
    [JsonProperty("pending_archives", Required = Required.DisallowNull)]
    public List<PuloniaTaskRunRecord> PendingArchives { get; set; } = [];

    /// <summary>
    /// 路线 CD 成功记录：每个（作用域，文件）只保留最新一条，判定本来就只看最新一条；可由用户清除；
    /// 每次成功的明细由运行历史中的确认事件保留。
    /// </summary>
    [JsonProperty("ledger", Required = Required.DisallowNull)]
    public List<PuloniaTaskLedgerEntry> Ledger { get; set; } = [];

    /// <summary>
    /// 已处理日程的定时触发游标，恢复、补触发和时钟回拨共用此游标；热键不写游标。
    /// 每个现有定时触发器至多一条，计划或触发器删除后由调度检查剔除。
    /// </summary>
    public List<PuloniaTaskTriggerState> TriggerStates { get; set; } = [];
}
