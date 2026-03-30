namespace BetterGenshinImpact.GameTask.AutoPathing.Suspend;

/// <summary>
/// 挂起接口 / Suspendable interface.
/// 定义了支持暂停与恢复操作的实体的基础规范。 / Defines the base contract for entities supporting suspend and resume operations.
/// </summary>
public interface ISuspendable
{
    /// <summary>
    /// 暂停操作 / Suspends the operation.
    /// </summary>
    void Suspend();

    /// <summary>
    /// 恢复操作 / Resumes the operation.
    /// </summary>
    void Resume();

    /// <summary>
    /// 是否处于暂停状态 / Gets a value indicating whether it is suspended.
    /// </summary>
    bool IsSuspended { get; }
}