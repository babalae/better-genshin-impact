namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 单个准备节点一次执行尝试的结果状态。
/// </summary>
public enum PuloniaTaskNodeStatus
{
    /// <summary>
    /// 节点因自身或祖先关闭而未执行。
    /// </summary>
    Skipped,

    /// <summary>
    /// 执行器返回成功结果。
    /// </summary>
    Succeeded,

    /// <summary>
    /// 执行器返回失败结果或抛出异常。
    /// </summary>
    Failed,

    /// <summary>
    /// 节点随运行取消，并已完成资源清理。
    /// </summary>
    Cancelled,

    /// <summary>
    /// 节点执行超过自身时限，并已完成资源清理。
    /// </summary>
    TimedOut
}
