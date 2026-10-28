using System.Collections.Generic;
using System;
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
    /// 服务器相对 UTC 的分钟偏移，用于计算路线 CD 的日、周重置窗口。
    /// 新运行入队时由任务服务按应用配置的服务器时区（ServerTimeHelper）统一填写，调用方的取值会被覆盖；续跑沿用原请求。
    /// </summary>
    public int ServerUtcOffsetMinutes { get; init; } = 8 * 60;

    /// <summary>
    /// 本次运行总时限，单位秒；null 表示默认不限时，显式设置时必须有限且大于 0。
    /// </summary>
    public double? TimeoutSeconds { get; init; }

    /// <summary>
    /// 以准备节点地址为键的本次调用参数覆盖，不回写计划。
    /// </summary>
    public Dictionary<string, JObject> ParameterOverrides { get; init; } = new();

    /// <summary>
    /// 便于界面区分来源的短文本，不参与执行逻辑。
    /// </summary>
    public string Source { get; init; } = "csharp";

    /// <summary>可选编辑树子任务/分组 ID。</summary>
    public string? TargetTaskId { get; init; }
    /// <summary>自动触发身份；手动运行为空。</summary>
    public string? TriggerId { get; init; }
    /// <summary>用于持久化去重的发生时刻。</summary>
    public DateTimeOffset? OccurrenceUtc { get; init; }
    /// <summary>最晚开始时间，不是运行总预算。</summary>
    public DateTimeOffset? DeadlineUtc { get; init; }
    /// <summary>自动触发配置的生效时间，禁用或编辑后不能启动旧排队请求。</summary>
    public DateTimeOffset? TriggerActivatedAtUtc { get; init; }
    /// <summary>本次自动请求的繁忙策略。</summary>
    public PuloniaTaskBusyPolicy BusyPolicy { get; init; }
    /// <summary>执行实例；为空时在主实例本地执行，否则由主实例编排、子实例执行叶子节点。</summary>
    public PuloniaTaskExecutionTarget? ExecutionTarget { get; init; }
}
