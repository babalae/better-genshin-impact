using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// GearTask 执行链路中的统一事件基类。
/// 执行器只负责发布事件，记录器、UI 投影和其他消费者按需订阅并消费。
/// </summary>
public abstract class GearTaskExecutionEvent
{
    /// <summary>
    /// 当前执行记录的唯一标识。
    /// </summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// 任务定义展示名。
    /// </summary>
    public string TaskDefinitionName { get; set; } = string.Empty;

    /// <summary>
    /// 任务定义对应的安全文件名，用于历史记录目录划分。
    /// </summary>
    public string TaskDefinitionFileKey { get; set; } = string.Empty;

    /// <summary>
    /// 任务树中的节点路径，例如 0/1/2。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    public string? ParentNodeId { get; set; }

    public string TaskName { get; set; } = string.Empty;

    public string TaskType { get; set; } = string.Empty;

    public string TaskPath { get; set; } = string.Empty;

    public int NodeDepth { get; set; }

    public int NodeOrder { get; set; }

    /// <summary>
    /// 事件发生时间，默认使用本地时间，便于直接展示在历史时间线中。
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// 预留给具体任务类型的扩展字段，避免事件模型为了单个任务不断膨胀。
    /// </summary>
    public Dictionary<string, object?> Extra { get; set; } = [];
}
