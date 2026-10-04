namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// Pulonia 请求从排队到完成的运行状态。
/// </summary>
public enum PuloniaTaskRunStatus
{
    /// <summary>
    /// 已固定运行快照，正在等待串行协调器执行。
    /// </summary>
    Queued,

    /// <summary>
    /// 正在执行快照中的任务节点。
    /// </summary>
    Running,

    /// <summary>
    /// 已收到取消请求，正在等待当前执行器释放资源。
    /// </summary>
    Cancelling,

    /// <summary>
    /// 全部应执行节点均已成功或按策略继续。
    /// </summary>
    Succeeded,

    /// <summary>
    /// 节点失败或执行基础设施发生错误。
    /// </summary>
    Failed,

    /// <summary>
    /// 已确认当前执行器退出并完成取消。
    /// </summary>
    Cancelled,

    /// <summary>
    /// 计划总运行时限耗尽，并已确认当前执行器退出。
    /// </summary>
    TimedOut,

    /// <summary>
    /// 应用退出或崩溃时仍在执行，不能推断副作用是否发生。
    /// </summary>
    Interrupted,

    /// <summary>
    /// 存在未决副作用，需要人工或能力核验后才能安全继续。
    /// </summary>
    NeedsAttention,

    /// <summary>自动请求超过最晚开始时间，没有启动执行器。</summary>
    Expired
}
