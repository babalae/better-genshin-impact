using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一条完成记录：某个账户最近一次完整执行成功某份路线的时间。账户进度 <c>route-completions.json</c> 中每份路线只保留最新一条；
/// 运行记录中“本次完成的路线”（<c>route_completions</c>）使用同一结构，保存每次成功的明细。
/// 写入后视为不可变：已读取的列表与缓存共用同一对象，只能整条替换或删除，不能原地修改。
/// </summary>
public sealed class PuloniaRouteCompletion
{
    /// <summary>路线身份：仓库路线 <c>repository:&lt;仓库 ID&gt;/&lt;小写相对路径&gt;</c>，本地路线 <c>local:&lt;小写绝对路径&gt;</c>。</summary>
    [JsonProperty("route", Required = Required.Always)]
    public string Route { get; init; } = string.Empty;

    /// <summary>完成时间（路线文件执行结束的时间），UTC。</summary>
    [JsonProperty("completed_at", Required = Required.Always)]
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>产生这条记录的运行 ID。</summary>
    [JsonProperty("run_id", Required = Required.Always)]
    public string RunId { get; init; } = string.Empty;

    /// <summary>产生这条记录的快照节点地址。</summary>
    [JsonProperty("task_address", Required = Required.Always)]
    public string TaskAddress { get; init; } = string.Empty;

    /// <summary>产生这条记录的尝试次数，从 1 开始；与运行 ID、节点地址一起作为幂等键。</summary>
    [JsonProperty("attempt", Required = Required.Always)]
    public int Attempt { get; init; }

    /// <summary>执行实例键，如 <c>primary</c>、<c>webview:小号a</c>。</summary>
    [JsonProperty("instance", Required = Required.Always)]
    public string Instance { get; init; } = string.Empty;

    /// <summary>记录时的刷新规则摘要，只用于在总览里显示“记录时规则”；判定 CD 时使用当前规则。</summary>
    [JsonProperty("rule", Required = Required.Always)]
    public PuloniaRouteCompletionRule Rule { get; init; } = new();

    /// <summary>
    /// 是否与另一条记录来自同一运行、同一节点、同一次尝试；重复写入时据此保持幂等。
    /// </summary>
    /// <param name="other">另一条完成记录。</param>
    public bool IsSameAttempt(PuloniaRouteCompletion other)
        => RunId == other.RunId && TaskAddress == other.TaskAddress && Attempt == other.Attempt;
}
