namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// 一次任务组执行开始。
/// </summary>
public sealed class ExecutionStartedEvent : GearTaskExecutionEvent
{
    /// <summary>
    /// 如果本次执行来自续跑，则记录源执行记录 Id。
    /// </summary>
    public string? SourceRecordId { get; set; }

    /// <summary>
    /// 本次预计可执行的总节点数，用于进度展示。
    /// </summary>
    public int TotalRunnableNodeCount { get; set; }
}

/// <summary>
/// 一次任务组执行正常结束。
/// </summary>
public sealed class ExecutionCompletedEvent : GearTaskExecutionEvent
{
}

/// <summary>
/// 一次任务组执行因异常失败。
/// </summary>
public sealed class ExecutionFailedEvent : GearTaskExecutionEvent
{
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 一次任务组执行被取消。
/// </summary>
public sealed class ExecutionCancelledEvent : GearTaskExecutionEvent
{
    public string? Reason { get; set; }
}

/// <summary>
/// 一次任务组执行被外部中断，但不一定等价于异常失败。
/// </summary>
public sealed class ExecutionInterruptedEvent : GearTaskExecutionEvent
{
    public string? Reason { get; set; }
}

/// <summary>
/// 单个任务节点开始执行。
/// </summary>
public sealed class TaskNodeStartedEvent : GearTaskExecutionEvent
{
}

/// <summary>
/// 单个任务节点执行完成。
/// </summary>
public sealed class TaskNodeCompletedEvent : GearTaskExecutionEvent
{
    public string? Message { get; set; }
}

/// <summary>
/// 单个任务节点执行失败。
/// </summary>
public sealed class TaskNodeFailedEvent : GearTaskExecutionEvent
{
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 单个任务节点被跳过。
/// </summary>
public sealed class TaskNodeSkippedEvent : GearTaskExecutionEvent
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 任务内部恢复点更新。
/// </summary>
public sealed class ResumePointUpdatedEvent : GearTaskExecutionEvent
{
    /// <summary>
    /// 是否支持在该任务内部继续执行，而不是仅支持节点级重跑。
    /// </summary>
    public bool CanResumeInsideTask { get; set; }

    /// <summary>
    /// 任务私有恢复状态序列化结果。
    /// </summary>
    public string? ResumeTokenJson { get; set; }

    public string? Message { get; set; }
}

/// <summary>
/// Pathing 进入某个 waypoint。
/// </summary>
public sealed class PathingWaypointEnteredEvent : GearTaskExecutionEvent
{
    public int WaypointGroupIndex { get; set; }

    public int WaypointIndex { get; set; }

    public string? Action { get; set; }

    public double PositionX { get; set; }

    public double PositionY { get; set; }
}

/// <summary>
/// Pathing 完成某个 waypoint。
/// </summary>
public sealed class PathingWaypointCompletedEvent : GearTaskExecutionEvent
{
    public int WaypointGroupIndex { get; set; }

    public int WaypointIndex { get; set; }

    public string? Action { get; set; }

    public double PositionX { get; set; }

    public double PositionY { get; set; }
}

/// <summary>
/// Pathing 完成传送步骤。
/// </summary>
public sealed class PathingTeleportCompletedEvent : GearTaskExecutionEvent
{
    public int WaypointGroupIndex { get; set; }

    public int WaypointIndex { get; set; }

    public double PositionX { get; set; }

    public double PositionY { get; set; }
}

/// <summary>
/// Pathing 完成 waypoint 绑定动作。
/// </summary>
public sealed class PathingActionCompletedEvent : GearTaskExecutionEvent
{
    public int WaypointGroupIndex { get; set; }

    public int WaypointIndex { get; set; }

    public string Action { get; set; } = string.Empty;

    public double PositionX { get; set; }

    public double PositionY { get; set; }
}

/// <summary>
/// JavaScript 脚本主动上报的检查点事件。
/// </summary>
public sealed class ScriptCheckpointReachedEvent : GearTaskExecutionEvent
{
    public string CheckpointName { get; set; } = string.Empty;

    public string? ResumeTokenJson { get; set; }

    public string? Message { get; set; }
}
