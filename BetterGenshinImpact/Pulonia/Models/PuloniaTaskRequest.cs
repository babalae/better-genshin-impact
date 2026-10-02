using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 由页面或进程内 C# 调用提交的 Pulonia 运行请求。
/// </summary>
public sealed class PuloniaTaskRequest
{
    /// <summary>
    /// 要运行的已保存计划 ID。
    /// </summary>
    public string PlanId { get; init; } = string.Empty;

    /// <summary>
    /// 可选账号资料 ID；步骤 3 只固定到快照，不启动或核验游戏账号。
    /// </summary>
    public string? AccountId { get; init; }

    /// <summary>
    /// 本次运行总时限，单位秒；必须有限且大于 0。
    /// </summary>
    public double TimeoutSeconds { get; init; } = 600;

    /// <summary>
    /// 以准备节点地址为键的本次调用参数覆盖，不回写计划。
    /// </summary>
    public Dictionary<string, JObject> ParameterOverrides { get; init; } = new();

    /// <summary>
    /// 便于界面区分来源的短文本，不参与执行逻辑。
    /// </summary>
    public string Source { get; init; } = "csharp";
}
