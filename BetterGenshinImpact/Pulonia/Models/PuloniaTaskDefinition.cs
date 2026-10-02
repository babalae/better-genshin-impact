using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 由调用端提供的类型说明；原型不注册真实游戏能力或执行器。
/// </summary>
public sealed class PuloniaTaskDefinition
{
    /// <summary>
    /// 任务类型。
    /// </summary>
    public string TaskType { get; init; } = string.Empty;

    /// <summary>
    /// 绑定的资源标识；JS 必须填写，并与节点 Path 一致。
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// 参数格式版本。
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// 类型默认参数。
    /// </summary>
    public JObject DefaultParameters { get; init; } = new();

    /// <summary>
    /// 受支持的 JSON Schema 子集：type、properties、required、additionalProperties、items、enum。
    /// </summary>
    public JObject ParameterSchema { get; init; } = new() { ["type"] = "object" };

    /// <summary>
    /// 允许由计划引用节点覆盖的公共参数名；其余参数只能在被引用计划内配置。
    /// </summary>
    public IReadOnlyList<string> PublicParameters { get; init; } = [];
}
