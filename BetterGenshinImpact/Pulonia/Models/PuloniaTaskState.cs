using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一个实例的运行状态文件模型，存在 <c>User/Pulonia/instances/&lt;实例目录名&gt;/state.json</c>，只由该实例写入。
/// </summary>
public sealed class PuloniaTaskState
{
    /// <summary>
    /// 当前支持的状态格式版本。4 起本实例的运行进入本实例的串行队列，活动运行只有一个，
    /// 并去掉通用额度框架的未决操作，触发游标只保存防重复字段；
    /// 5 起请求不再带账户 ID，运行记录增加运行账户、来源与 UID 比对结果；
    /// 6 起删除 <c>ledger</c>（完成记录移到各账户的 route-completions.json），运行记录的 <c>confirmed_effects</c> 改为 <c>route_completions</c>；
    /// 7 起请求不再带 <c>execution_target</c>（逐节点派发已删除），运行记录增加执行实例 <c>instance</c>。
    /// 开发期不转换旧版本，读取只接受当前版本；旧文件需要删除后重建。
    /// </summary>
    public const int CurrentSchemaVersion = 7;

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
    /// 本实例队列中唯一的活动运行；主实例下发到本实例的运行同样在本实例的队列中执行。
    /// </summary>
    [JsonProperty("active_run")]
    public PuloniaTaskRunRecord? ActiveRun { get; set; }

    /// <summary>
    /// 已结束但尚未确认写入不可变历史文件的运行。
    /// </summary>
    [JsonProperty("pending_archives", Required = Required.DisallowNull)]
    public List<PuloniaTaskRunRecord> PendingArchives { get; set; } = [];

    // 完成记录属于账户进度，存在 accounts/<账户 ID>/route-completions.json，不在实例运行状态中。

    /// <summary>
    /// 已处理日程的定时触发游标，恢复、补触发和时钟回拨共用此游标；热键不写游标。
    /// 每个现有定时触发器至多一条，计划或触发器删除后由调度检查剔除。
    /// </summary>
    public List<PuloniaTaskTriggerState> TriggerStates { get; set; } = [];
}
