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

    /// <summary>提交时固定的原神账号准备资料 JSON；为空表示普通计划未启用账号准备。</summary>
    [JsonProperty("genshin_account_json")]
    public string? GenshinAccountJson { get; }

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
    /// 旧版快照固定的预设 JSON；预设已由设置方案取代，新快照始终为空，仅为读取旧历史保留。
    /// </summary>
    [JsonProperty("preset_json_by_id")]
    public IReadOnlyDictionary<string, string> PresetJsonById { get; }

    /// <summary>
    /// 实际使用的设置方案 JSON，包含修订；修改方案不影响已固定的快照。
    /// </summary>
    [JsonProperty("profile_json_by_id")]
    public IReadOnlyDictionary<string, string> ProfileJsonById { get; }

    /// <summary>没有使用方案时不写出，旧快照读入后再写出保持逐字一致。</summary>
    public bool ShouldSerializeProfileJsonById() => ProfileJsonById.Count > 0;

    /// <summary>
    /// 复制来源记录并建立只读快照。
    /// </summary>
    internal PuloniaTaskSnapshot(PuloniaTaskPlan plan, string? accountId, PuloniaTaskPreparedTask root,
        Dictionary<string, string> plans, Dictionary<string, string> profiles)
    {
        PlanId = plan.Id;
        Revision = plan.Revision;
        PreparedAt = DateTimeOffset.UtcNow;
        AccountId = accountId;
        RootTask = root;
        PlanJsonById = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(plans));
        PresetJsonById = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        ProfileJsonById = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(profiles));
    }

    /// <summary>
    /// 从持久化运行快照恢复不可变对象；旧快照没有方案字段时视为空。
    /// </summary>
    [JsonConstructor]
    internal PuloniaTaskSnapshot(string planId, long revision, DateTimeOffset preparedAt, string? accountId,
        PuloniaTaskPreparedTask rootTask, IDictionary<string, string> planJsonById,
        IDictionary<string, string>? presetJsonById, string? genshinAccountJson = null,
        IDictionary<string, string>? profileJsonById = null)
    {
        PlanId = planId;
        Revision = revision;
        PreparedAt = preparedAt;
        AccountId = accountId;
        GenshinAccountJson = genshinAccountJson;
        RootTask = rootTask;
        PlanJsonById = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(planJsonById));
        PresetJsonById = new ReadOnlyDictionary<string, string>(presetJsonById is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(presetJsonById));
        ProfileJsonById = new ReadOnlyDictionary<string, string>(profileJsonById is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(profileJsonById));
    }

    /// <summary>
    /// 输出运行快照供持久化和续跑。
    /// </summary>
    public string ToJson() => PuloniaTaskJson.Write(this);

    /// <summary>用独立的不可变 JSON 固定账号资料，不暴露可被编辑的模型对象。</summary>
    internal PuloniaTaskSnapshot WithGenshinAccount(PuloniaGenshinAccountProfile account)
        => new(PlanId, Revision, PreparedAt, AccountId, RootTask,
            new Dictionary<string, string>(PlanJsonById), new Dictionary<string, string>(PresetJsonById),
            PuloniaTaskJson.Write(account), new Dictionary<string, string>(ProfileJsonById));
}
