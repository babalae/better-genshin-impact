using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一条结构化完成证据，保留观测来源和发生时间。
/// </summary>
public sealed class PuloniaTaskEvidence
{
    /// <summary>
    /// 证据类型，例如 visual、counter、executor 或 manual。
    /// </summary>
    [JsonProperty("kind", Required = Required.Always)]
    public string Kind { get; set; } = "executor";

    /// <summary>
    /// 产生证据的能力、脚本或人工入口。
    /// </summary>
    [JsonProperty("source", Required = Required.Always)]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 证据对应事件的 UTC 时间。
    /// </summary>
    [JsonProperty("occurred_at", Required = Required.Always)]
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 证据的结构化附加数据。
    /// </summary>
    [JsonProperty("data", Required = Required.DisallowNull)]
    public JObject Data { get; set; } = new();
}
