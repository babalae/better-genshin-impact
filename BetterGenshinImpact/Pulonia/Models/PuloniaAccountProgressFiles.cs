using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 账户进度中的完成记录文件 <c>User/Pulonia/accounts/&lt;账户 ID&gt;/route-completions.json</c>：每份路线只保留最新一条。
/// </summary>
public sealed class PuloniaRouteCompletionFile
{
    /// <summary>当前支持的格式版本。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>格式版本，缺失或不同时拒绝读取。</summary>
    [JsonProperty("schema_version", Required = Required.Always)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>完成记录；路线身份不重复。</summary>
    [JsonProperty("completions", Required = Required.Always, ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<PuloniaRouteCompletion> Completions { get; set; } = [];
}

/// <summary>
/// 账户进度中的首领领奖次数文件 <c>User/Pulonia/accounts/&lt;账户 ID&gt;/boss-rewards.json</c>。
/// </summary>
public sealed class PuloniaBossRewardFile
{
    /// <summary>当前支持的格式版本。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>格式版本，缺失或不同时拒绝读取。</summary>
    [JsonProperty("schema_version", Required = Required.Always)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>各（计划、节点、重置批次）的累计次数；键不重复。</summary>
    [JsonProperty("rewards", Required = Required.Always, ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<PuloniaBossRewardEntry> Rewards { get; set; } = [];
}

/// <summary>
/// 一个首领讨伐节点在一个重置批次内的累计成功领奖次数。
/// </summary>
public sealed class PuloniaBossRewardEntry
{
    /// <summary>
    /// 由（计划 ID、节点 ID、重置批次）计算的 SHA-256 十六进制字符串（见 PuloniaTaskStore.GetBossRewardKey），
    /// 与旧 <c>boss-rewards/&lt;哈希&gt;.json</c> 的文件名相同，执行器和迁移得到同一个键。
    /// </summary>
    [JsonProperty("key", Required = Required.Always)]
    public string Key { get; set; } = string.Empty;

    /// <summary>累计成功领奖次数，不为负。</summary>
    [JsonProperty("count", Required = Required.Always)]
    public int Count { get; set; }

    /// <summary>计划 ID；新写入时记录，迁移来的旧条目没有。</summary>
    [JsonProperty("plan_id", NullValueHandling = NullValueHandling.Ignore)]
    public string? PlanId { get; set; }

    /// <summary>节点 ID；新写入时记录，迁移来的旧条目没有。</summary>
    [JsonProperty("task_id", NullValueHandling = NullValueHandling.Ignore)]
    public string? TaskId { get; set; }

    /// <summary>重置批次；新写入时记录，迁移来的旧条目没有。</summary>
    [JsonProperty("reset_id", NullValueHandling = NullValueHandling.Ignore)]
    public string? ResetId { get; set; }
}
