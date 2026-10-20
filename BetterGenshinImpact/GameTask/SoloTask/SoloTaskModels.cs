using Newtonsoft.Json.Linq;
using System;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 可从独立任务页面启动的任务类型。
/// </summary>
public enum SoloTaskType
{
    AutoGeniusInvokation,
    AutoWood,
    AutoFight,
    AutoDomain,
    AutoBoss,
    AutoStygianOnslaught,
    AutoFishing,
    AutoLeyLineOutcrop,
    AutoMusicGame,
    AutoAlbum,
    AutoCook,
    AutoCombo,
    AutoArtifactSalvage,
    AutoRedeemCode,
    GetGridIcons,
    GridIconsAccuracyTest,
    InventoryCountComparison
}

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
/// 独立任务运行结束原因。
/// </summary>
public enum SoloTaskRunOutcome
{
    Completed,
    Cancelled,
    Failed,
    Rejected
}

/// <summary>
/// 创建多实例运行请求时固定下来的任务参数。
/// </summary>
public sealed class SoloTaskStartDefinition
{
    /// <summary>
    /// 要运行的独立任务类型。
    /// </summary>
    public SoloTaskType TaskType { get; init; }

    /// <summary>
    /// 不属于持久化配置的任务专用参数。
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
            TaskType = TaskType,
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
    /// 要运行的独立任务类型。
    /// </summary>
    public SoloTaskType TaskType { get; init; }

    /// <summary>
    /// 不属于持久化配置的任务专用参数。
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
    /// 当前任务类型；空闲时为 null。
    /// </summary>
    public SoloTaskType? TaskType { get; init; }

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
    public bool IsActive => State != SoloTaskRunState.Idle && RunId is not null && TaskType is not null;

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
    SoloTaskType taskType,
    SoloTaskRunResult result) : EventArgs
{
    /// <summary>
    /// 已结束运行的唯一标识。
    /// </summary>
    public Guid RunId { get; } = runId;

    /// <summary>
    /// 已结束的任务类型。
    /// </summary>
    public SoloTaskType TaskType { get; } = taskType;

    /// <summary>
    /// 本次运行的明确结束原因。
    /// </summary>
    public SoloTaskRunResult Result { get; } = result;
}

/// <summary>
/// TaskRunner 返回给运行协调器的执行结果。
/// </summary>
public sealed class SoloTaskRunResult
{
    /// <summary>
    /// 本次执行的结束原因。
    /// </summary>
    public SoloTaskRunOutcome Outcome { get; init; }

    /// <summary>
    /// 执行失败时捕获的异常消息。
    /// </summary>
    public string? ErrorMessage { get; init; }
}
