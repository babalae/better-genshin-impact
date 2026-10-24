using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// Pulonia 的唯一当前运行状态文件模型。
/// </summary>
public sealed class PuloniaTaskState
{
    /// <summary>
    /// 当前支持的状态格式版本。3 起增加远程活动运行；读取 2 时自动升级。
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>
    /// 可以直接升级读取的上一个格式版本。
    /// </summary>
    public const int PreviousSchemaVersion = 2;

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
    /// 主实例本地通道的唯一活动运行。
    /// </summary>
    [JsonProperty("active_run")]
    public PuloniaTaskRunRecord? ActiveRun { get; set; }

    /// <summary>
    /// 在子实例上执行的活动运行，每个执行实例最多一个。旧格式没有此字段时为空集合。
    /// </summary>
    [JsonProperty("remote_active_runs", Required = Required.DisallowNull)]
    public List<PuloniaTaskRunRecord> RemoteActiveRuns { get; set; } = [];

    /// <summary>
    /// 已结束但尚未确认写入不可变历史文件的运行。
    /// </summary>
    [JsonProperty("pending_archives", Required = Required.DisallowNull)]
    public List<PuloniaTaskRunRecord> PendingArchives { get; set; } = [];

    /// <summary>
    /// 仍约束当前执行的 CD 与额度事实。
    /// </summary>
    [JsonProperty("ledger", Required = Required.DisallowNull)]
    public List<PuloniaTaskLedgerEntry> Ledger { get; set; } = [];

    /// <summary>
    /// 崩溃恢复后尚未核验或由用户明确释放的操作意图。
    /// </summary>
    [JsonProperty("uncertain_operations", Required = Required.DisallowNull)]
    public List<PuloniaTaskOperationIntent> UncertainOperations { get; set; } = [];

    /// <summary>已处理日程与待提交发生项，恢复、补触发和时钟回拨共用此游标。</summary>
    public List<PuloniaTaskTriggerState> TriggerStates { get; set; } = [];
}
