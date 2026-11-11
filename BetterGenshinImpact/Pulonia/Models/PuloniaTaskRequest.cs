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
    /// 主实例一步下发（<c>pulonia.run.submit</c>）到本实例的请求来源。子实例只向主实例回报、只允许主实例取消这一来源的运行，
    /// 重新连上主实例后也按它补发尚未结束的运行状态。
    /// </summary>
    public const string DispatchSource = "dispatch";

    /// <summary>
    /// 要运行的已保存计划 ID。
    /// </summary>
    public string PlanId { get; init; } = string.Empty;

    // 请求不再携带账户 ID：运行账户只由计划的运行账户设置和实例当前账户在运行开始时决定，续跑按原运行确定。

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

    /// <summary>
    /// 切换前检查（统筹设计 4.6）的人工确认：指定账户的运行需要切换到某个账户，而另一个在线实例的当前账户就是它时，
    /// 切换会把那个实例挤下线。只有在本实例界面手动运行、并在提交前确认过时为 true；触发器、命令行、主实例下发与续跑总为 false，
    /// 这时不切换，运行进入待处理。
    /// </summary>
    public bool AllowDisplaceOnlineInstance { get; init; }

    // 请求不再携带执行实例：运行总在提交它的实例执行，执行实例记在运行记录的 instance；
    // 主实例要在子实例运行时经 pulonia.run.submit 整次下发，由子实例在本地提交。
}
