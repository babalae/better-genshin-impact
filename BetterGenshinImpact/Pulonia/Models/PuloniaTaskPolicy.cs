using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 按字段继承的执行策略；空字段表示未覆盖。
/// </summary>
public sealed class PuloniaTaskPolicy
{
    /// <summary>
    /// 节点时限，单位秒，必须大于 0。
    /// </summary>
    [JsonProperty("timeout_seconds")]
    public double? TimeoutSeconds { get; set; }

    /// <summary>
    /// 失败后行为：stop_plan、skip_group 或 continue。
    /// </summary>
    [JsonProperty("failure_behavior")]
    public string? FailureBehavior { get; set; }

    /// <summary>
    /// 有限重试次数；0 表示不重试。
    /// </summary>
    [JsonProperty("max_retries")]
    public int? MaxRetries { get; set; }

    /// <summary>
    /// 重试间隔，单位秒，允许显式设置为 0。
    /// </summary>
    [JsonProperty("retry_delay_seconds")]
    public double? RetryDelaySeconds { get; set; }
}
