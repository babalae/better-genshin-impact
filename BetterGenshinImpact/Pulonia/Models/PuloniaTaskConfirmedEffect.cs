using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一次节点尝试已耐久确认的副作用，随运行归档，不依赖当前 CD 账本的保留策略。
/// </summary>
public sealed class PuloniaTaskConfirmedEffect
{
    /// <summary>
    /// 产生确认事件的快照节点地址。
    /// </summary>
    [JsonProperty("task_address", Required = Required.Always)]
    public string TaskAddress { get; init; } = string.Empty;

    /// <summary>
    /// 产生确认事件的尝试次数，从 1 开始。
    /// </summary>
    [JsonProperty("attempt", Required = Required.Always)]
    public int Attempt { get; init; }

    /// <summary>
    /// 事件键、额度、作用域、时间与证据的固定副本。
    /// </summary>
    [JsonProperty("entry", Required = Required.Always)]
    public PuloniaTaskLedgerEntry Entry { get; init; } = new();
}
