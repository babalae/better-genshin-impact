using System;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 单个准备节点一次执行尝试的只读结果。
/// </summary>
public sealed class PuloniaTaskNodeResult
{
    /// <summary>
    /// 快照中的稳定任务地址，包含引用路径和重复轮次。
    /// </summary>
    public string TaskAddress { get; }

    /// <summary>
    /// 快照固定的节点名称。
    /// </summary>
    public string TaskName { get; }

    /// <summary>
    /// 节点任务类型。
    /// </summary>
    public string TaskType { get; }

    /// <summary>
    /// 本节点从 1 开始的尝试次数；禁用节点为 0。
    /// </summary>
    public int Attempt { get; }

    /// <summary>
    /// 本次尝试的最终状态。
    /// </summary>
    public PuloniaTaskNodeStatus Status { get; }

    /// <summary>
    /// 执行结果、异常、取消或超时原因。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 尝试开始时间，使用 UTC。
    /// </summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// 尝试结束时间，使用 UTC。
    /// </summary>
    public DateTimeOffset FinishedAt { get; }

    /// <summary>
    /// 执行器附带的结构化结果副本。
    /// </summary>
    public JObject Data { get; }

    /// <summary>
    /// 建立不可变节点结果。
    /// </summary>
    public PuloniaTaskNodeResult(string taskAddress, string taskName, string taskType, int attempt,
        PuloniaTaskNodeStatus status, string message, DateTimeOffset startedAt, DateTimeOffset finishedAt,
        JObject? data = null)
    {
        TaskAddress = taskAddress;
        TaskName = taskName;
        TaskType = taskType;
        Attempt = attempt;
        Status = status;
        Message = message;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        Data = data is null ? new JObject() : (JObject)data.DeepClone();
    }
}
