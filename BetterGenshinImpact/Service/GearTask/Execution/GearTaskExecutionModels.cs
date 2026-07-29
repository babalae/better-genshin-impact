using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// 一次任务组执行记录的状态。
/// </summary>
public enum GearTaskExecutionRecordStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted,
}

/// <summary>
/// 单个任务节点在执行记录中的状态。
/// </summary>
public enum GearTaskExecutionNodeStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled,
    Interrupted,
}

/// <summary>
/// 一次大任务组执行的持久化记录。
/// </summary>
public class GearTaskExecutionRecord
{
    public string RecordId { get; set; } = string.Empty;

    public string TaskDefinitionName { get; set; } = string.Empty;

    public string TaskDefinitionFileKey { get; set; } = string.Empty;

    public string? SourceRecordId { get; set; }

    public GearTaskExecutionRecordStatus Status { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public int TotalRunnableNodeCount { get; set; }

    public int CompletedNodeCount { get; set; }

    public int FailedNodeCount { get; set; }

    public int SkippedNodeCount { get; set; }

    public string? CurrentNodeId { get; set; }

    /// <summary>
    /// 当前记录可恢复时，对应的节点 Id。
    /// </summary>
    public string? ResumeNodeId { get; set; }

    /// <summary>
    /// 是否存在有效恢复点。
    /// </summary>
    public bool CanResume { get; set; }

    public string? InterruptReason { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 当前记录内所有节点的最终或最近状态快照。
    /// </summary>
    public List<GearTaskExecutionNodeRecord> Nodes { get; set; } = [];

    /// <summary>
    /// 完整时间线，面向执行记录详情和调试定位。
    /// </summary>
    public List<GearTaskExecutionTimelineItem> Timeline { get; set; } = [];

    public int SchemaVersion { get; set; } = 1;
}

/// <summary>
/// 单个任务节点的持久化快照。
/// </summary>
public class GearTaskExecutionNodeRecord
{
    public string NodeId { get; set; } = string.Empty;

    public string? ParentNodeId { get; set; }

    public string TaskName { get; set; } = string.Empty;

    public string TaskType { get; set; } = string.Empty;

    public string TaskPath { get; set; } = string.Empty;

    public int Depth { get; set; }

    public int Order { get; set; }

    public GearTaskExecutionNodeStatus Status { get; set; }

    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; set; }

    public bool CanResumeInsideTask { get; set; }

    /// <summary>
    /// 节点内部恢复状态，通常由具体任务类型自己定义。
    /// </summary>
    public string? ResumeTokenJson { get; set; }
}

/// <summary>
/// 面向“执行详情”视图的时间线条目。
/// </summary>
public class GearTaskExecutionTimelineItem
{
    public DateTime Timestamp { get; set; }

    public string NodeId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public Dictionary<string, object?> Extra { get; set; } = [];
}

/// <summary>
/// 执行恢复计划。
/// 由历史记录解析得到，再传递给执行器决定哪些节点跳过、哪些节点恢复。
/// </summary>
public class GearTaskResumePlan
{
    public string SourceRecordId { get; set; } = string.Empty;

    public string ResumeNodeId { get; set; } = string.Empty;

    public Dictionary<string, string> NodeResumeTokens { get; set; } = [];
}

/// <summary>
/// 一次执行运行时的公共上下文。
/// </summary>
public sealed class GearTaskExecutionRunContext
{
    public string RecordId { get; init; } = string.Empty;

    public string TaskDefinitionName { get; init; } = string.Empty;

    public string TaskDefinitionFileKey { get; init; } = string.Empty;

    public GearTaskResumePlan? ResumePlan { get; init; }
}

/// <summary>
/// PathingGearTask 的内部恢复状态。
/// 当前按 waypoint 粒度记录，后续需要更细时可继续扩展。
/// </summary>
public class PathingGearTaskResumeState
{
    public string PathFile { get; set; } = string.Empty;

    public int WaypointGroupIndex { get; set; }

    public int WaypointIndex { get; set; }

    public bool SkipOtherOperations { get; set; }

    public string? LastAction { get; set; }

    public DateTime CaptureTime { get; set; }
}
