using Newtonsoft.Json.Linq;
using System;

namespace BetterGenshinImpact.GameTask.SoloTask.Execution;

/// <summary>
/// 一个实例中的独立任务运行状态。
/// </summary>
public enum SoloTaskRunState
{
    Idle,
    Starting,
    Running,
    Stopping
}

/// <summary>
/// 独立任务运行在运行层的结束原因。任务自己的业务结果见 <see cref="SoloTaskRunResult.TaskResult"/>。
/// </summary>
public enum SoloTaskRunOutcome
{
    /// <summary>
    /// 运行正常结束；通过 RunSoloTaskAsync 运行时 TaskResult 为任务结果。
    /// </summary>
    Completed,

    /// <summary>
    /// 运行被取消或运行环境停止。
    /// </summary>
    Cancelled,

    /// <summary>
    /// 运行中抛出了未处理的异常。
    /// </summary>
    Faulted,

    /// <summary>
    /// 当前实例已有任务在运行，本次被拒绝。
    /// </summary>
    Rejected
}

/// <summary>
/// 创建多实例运行请求时固定下来的任务参数。
/// </summary>
public sealed class SoloTaskStartDefinition
{
    /// <summary>
    /// 要运行的独立任务标识。
    /// </summary>
    public string TaskId { get; init; } = string.Empty;

    /// <summary>
    /// 按 SoloTaskJson 约定序列化的完整任务参数；为空时使用参数默认值。
    /// </summary>
    public JObject? Parameters { get; init; }

    /// <summary>
    /// 为某个实例创建拥有独立运行标识的启动请求。
    /// </summary>
    public SoloTaskStartRequest CreateRequest()
    {
        return new SoloTaskStartRequest
        {
            RunId = Guid.NewGuid(),
            TaskId = TaskId,
            Parameters = Parameters is null ? null : (JObject)Parameters.DeepClone()
        };
    }
}

/// <summary>
/// 发送给目标实例的独立任务启动请求。
/// </summary>
public sealed class SoloTaskStartRequest
{
    /// <summary>
    /// 本次运行的全局唯一标识，用于启动和停止幂等。
    /// </summary>
    public Guid RunId { get; init; }

    /// <summary>
    /// 要运行的独立任务标识。
    /// </summary>
    public string TaskId { get; init; } = string.Empty;

    /// <summary>
    /// 按 SoloTaskJson 约定序列化的完整任务参数；为空时使用参数默认值。
    /// </summary>
    public JObject? Parameters { get; init; }
}

/// <summary>
/// 停止目标实例指定独立任务运行的请求。
/// </summary>
public sealed class SoloTaskStopRequest
{
    /// <summary>
    /// 需要停止的运行标识。
    /// </summary>
    public Guid RunId { get; init; }
}

/// <summary>
/// 一个实例当前独立任务状态的不可变快照。
/// </summary>
public sealed class SoloTaskStatusSnapshot
{
    /// <summary>
    /// 当前进程内单调递增的状态版本，用于拒绝乱序到达的旧状态。
    /// </summary>
    public long Version { get; init; }

    /// <summary>
    /// 当前运行标识；空闲时为 null。
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>
    /// 当前任务标识；空闲时为 null。
    /// </summary>
    public string? TaskId { get; init; }

    /// <summary>
    /// 当前运行状态。
    /// </summary>
    public SoloTaskRunState State { get; init; }

    /// <summary>
    /// 最近一次运行失败的简短错误信息。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 当前实例是否仍被一个独立任务占用。
    /// </summary>
    public bool IsActive => State != SoloTaskRunState.Idle && RunId is not null && TaskId is not null;

    /// <summary>
    /// 创建空闲状态快照。
    /// </summary>
    public static SoloTaskStatusSnapshot Idle(string? errorMessage = null, long version = 0)
    {
        return new SoloTaskStatusSnapshot
        {
            Version = version,
            State = SoloTaskRunState.Idle,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// 独立任务启动或停止请求的处理结果。
/// </summary>
public sealed class SoloTaskOperationResponse
{
    /// <summary>
    /// 请求是否已被目标实例接受。
    /// </summary>
    public bool Accepted { get; init; }

    /// <summary>
    /// 请求处理后的目标实例状态。
    /// </summary>
    public SoloTaskStatusSnapshot Status { get; init; } = SoloTaskStatusSnapshot.Idle();

    /// <summary>
    /// 请求被拒绝时供 UI 展示的原因。
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 独立任务状态发生变化时的事件参数。
/// </summary>
public sealed class SoloTaskStatusChangedEventArgs(SoloTaskStatusSnapshot status) : EventArgs
{
    /// <summary>
    /// 变化后的状态快照。
    /// </summary>
    public SoloTaskStatusSnapshot Status { get; } = status;
}

/// <summary>
/// 一个独立任务运行完成、取消、失败或被执行层拒绝时的事件参数。
/// </summary>
public sealed class SoloTaskRunCompletedEventArgs(
    Guid runId,
    string taskId,
    SoloTaskRunResult result) : EventArgs
{
    /// <summary>
    /// 已结束运行的唯一标识。
    /// </summary>
    public Guid RunId { get; } = runId;

    /// <summary>
    /// 已结束的任务标识。
    /// </summary>
    public string TaskId { get; } = taskId;

    /// <summary>
    /// 本次运行的明确结束原因。
    /// </summary>
    public SoloTaskRunResult Result { get; } = result;
}

/// <summary>
/// TaskRunner 返回的一次独立运行结果。
/// </summary>
public sealed class SoloTaskRunResult
{
    /// <summary>
    /// 运行层的结束原因。
    /// </summary>
    public SoloTaskRunOutcome Outcome { get; init; }

    /// <summary>
    /// 取消、异常或拒绝的原因；任务返回失败结果时为失败原因。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 任务自己的结果；只有通过 RunSoloTaskAsync 运行且结束原因为 Completed 时才有值。
    /// </summary>
    public ISoloTaskResult? TaskResult { get; init; }
}
