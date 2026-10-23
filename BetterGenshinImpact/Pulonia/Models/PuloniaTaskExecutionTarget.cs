using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 运行请求的执行实例。请求未指定时在主实例本地执行；指定后由主实例编排，叶子节点交给该子实例执行。
/// 进程 ID 每次重启都会变化，因此用实例类型加稳定标识（云原神实例名、桌面分身会话）描述目标
/// </summary>
public sealed class PuloniaTaskExecutionTarget
{
    /// <summary>
    /// 子实例类型
    /// </summary>
    [JsonProperty("kind", Required = Required.Always)]
    [JsonConverter(typeof(StringEnumConverter), typeof(SnakeCaseNamingStrategy))]
    public PuloniaTaskExecutionTargetKind Kind { get; init; }

    /// <summary>
    /// 云原神实例名；桌面分身为空
    /// </summary>
    [JsonProperty("instance_name")]
    public string? InstanceName { get; init; }

    /// <summary>
    /// 桌面分身所在的 Windows 会话；云原神为空
    /// </summary>
    [JsonProperty("windows_session_id")]
    public int? WindowsSessionId { get; init; }

    /// <summary>
    /// 提交时的显示名，只用于界面和记录展示
    /// </summary>
    [JsonProperty("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// 执行通道标识：同一通道内的运行串行，不同通道可以并行
    /// </summary>
    [JsonIgnore]
    public string LaneKey => Kind switch
    {
        PuloniaTaskExecutionTargetKind.ChildSession => "child_session:" + WindowsSessionId,
        PuloniaTaskExecutionTargetKind.WebView => "webview:" + (InstanceName ?? string.Empty).ToLowerInvariant(),
        _ => "unknown"
    };

    /// <summary>
    /// 主实例本地执行通道标识
    /// </summary>
    public const string PrimaryLaneKey = "primary";

    /// <summary>
    /// 返回请求所属的执行通道；未指定目标时为主实例通道
    /// </summary>
    /// <param name="target">请求的执行实例</param>
    /// <returns>执行通道标识</returns>
    public static string GetLaneKey(PuloniaTaskExecutionTarget? target) => target?.LaneKey ?? PrimaryLaneKey;

    /// <summary>
    /// 校验目标字段：桌面分身必须带会话，云原神必须带实例名
    /// </summary>
    /// <exception cref="PuloniaTaskValidationException">字段缺失或无效</exception>
    public void Validate()
    {
        switch (Kind)
        {
            case PuloniaTaskExecutionTargetKind.ChildSession when WindowsSessionId is null or < 0:
                throw new PuloniaTaskValidationException("request/execution_target", "桌面分身执行目标缺少会话标识。");
            case PuloniaTaskExecutionTargetKind.WebView when string.IsNullOrWhiteSpace(InstanceName):
                throw new PuloniaTaskValidationException("request/execution_target", "云原神执行目标缺少实例名。");
            case PuloniaTaskExecutionTargetKind.ChildSession:
            case PuloniaTaskExecutionTargetKind.WebView:
                return;
            default:
                throw new PuloniaTaskValidationException("request/execution_target", "执行目标类型无效。");
        }
    }

    /// <summary>
    /// 复制一份独立对象，避免请求之间共享可变引用
    /// </summary>
    /// <returns>副本</returns>
    public PuloniaTaskExecutionTarget Clone() => new()
    {
        Kind = Kind,
        InstanceName = InstanceName,
        WindowsSessionId = WindowsSessionId,
        DisplayName = DisplayName
    };

    /// <summary>
    /// 是否与另一个目标指向同一执行通道
    /// </summary>
    /// <param name="other">另一个目标</param>
    /// <returns>同一通道时返回 true</returns>
    public bool IsSameLane(PuloniaTaskExecutionTarget? other)
        => string.Equals(GetLaneKey(this), GetLaneKey(other), StringComparison.Ordinal);
}

/// <summary>
/// 可以执行 Pulonia 节点的子实例类型
/// </summary>
public enum PuloniaTaskExecutionTargetKind
{
    /// <summary>
    /// 桌面分身（另一个 Windows 会话中的 BetterGI）
    /// </summary>
    ChildSession,

    /// <summary>
    /// 云原神网页版实例
    /// </summary>
    WebView
}
