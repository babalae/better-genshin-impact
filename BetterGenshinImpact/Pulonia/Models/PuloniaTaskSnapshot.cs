using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 提交运行时固定的不可变、可序列化快照。
/// </summary>
public sealed class PuloniaTaskSnapshot
{
    /// <summary>
    /// 入口计划 ID。
    /// </summary>
    [JsonProperty("plan_id")]
    public string PlanId { get; }

    /// <summary>
    /// 入口计划内容修订号。
    /// </summary>
    [JsonProperty("revision")]
    public long Revision { get; }

    /// <summary>
    /// 准备完成时间，使用 UTC。
    /// </summary>
    [JsonProperty("prepared_at")]
    public DateTimeOffset PreparedAt { get; }

    /// <summary>
    /// 本次选择的账号资料 ID，不代表已经核验游戏 UID。
    /// </summary>
    [JsonProperty("account_id")]
    public string? AccountId { get; }

    /// <summary>
    /// 固定后的只读执行树。
    /// </summary>
    [JsonProperty("root_task")]
    public PuloniaTaskPreparedTask RootTask { get; }

    /// <summary>
    /// 入口及引用计划的原始 JSON，包含修订和账号绑定。
    /// </summary>
    [JsonProperty("plan_json_by_id")]
    public IReadOnlyDictionary<string, string> PlanJsonById { get; }

    /// <summary>
    /// 实际使用的预设 JSON，包含修订及参数格式版本。
    /// </summary>
    [JsonProperty("preset_json_by_id")]
    public IReadOnlyDictionary<string, string> PresetJsonById { get; }

    /// <summary>
    /// 复制来源记录并建立只读快照。
    /// </summary>
    internal PuloniaTaskSnapshot(PuloniaTaskPlan plan, string? accountId, PuloniaTaskPreparedTask root,
        Dictionary<string, string> plans, Dictionary<string, string> presets)
    {
        PlanId = plan.Id;
        Revision = plan.Revision;
        PreparedAt = DateTimeOffset.UtcNow;
        AccountId = accountId;
        RootTask = root;
        PlanJsonById = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(plans));
        PresetJsonById = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(presets));
    }

    /// <summary>
    /// 输出运行快照供查看和后续持久化；恢复绑定属于步骤 6。
    /// </summary>
    public string ToJson() => PuloniaTaskJson.Write(this);
}
