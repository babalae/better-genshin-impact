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
    TimedOut
}
