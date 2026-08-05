using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.TargetNavigation;

public enum TargetNavigationState
{
    NoTarget,
    WaitingToStart,
    Planning,
    PlanFailed,
    PlanSucceeded,
    Executing,
    Completed,
    ExecutionFailed,
    UserCancelled
}

public enum TargetNavigationFailureCode
{
    None,
    TargetNotSelected,
    GraphFileMissing,
    GraphEmpty,
    GraphInvalid,
    GraphNotLoaded,
    CurrentPositionUnrecognized,
    CurrentPointNotConnected,
    TargetPointNotConnected,
    MapMismatch,
    NoRoute,
    TeleportUnavailable,
    CoordinateConversionFailed,
    PlannedTaskInvalid,
    CaptureNotInitialized,
    GameWindowNotFound,
    NotInMainUi,
    TaskRunnerBusy,
    WindowActivationFailed,
    GameWindowLostFocus,
    ExecutionFailed,
    UserCancelled,
    Unexpected
}

public static class TargetNavigationFailureMessages
{
    public static string Format(TargetNavigationFailureCode code, string? detail = null)
    {
        var message = code switch
        {
            TargetNavigationFailureCode.None => string.Empty,
            TargetNavigationFailureCode.TargetNotSelected => "未选择目标",
            TargetNavigationFailureCode.GraphFileMissing => "路网文件不存在",
            TargetNavigationFailureCode.GraphEmpty => "路网为空",
            TargetNavigationFailureCode.GraphInvalid => "路网文件格式无效",
            TargetNavigationFailureCode.GraphNotLoaded => "路网尚未加载",
            TargetNavigationFailureCode.CurrentPositionUnrecognized => "当前坐标不可识别",
            TargetNavigationFailureCode.CurrentPointNotConnected => "当前点无法接入路网",
            TargetNavigationFailureCode.TargetPointNotConnected => "目标点无法接入路网",
            TargetNavigationFailureCode.MapMismatch => "当前地图和目标地图不一致",
            TargetNavigationFailureCode.NoRoute => "没有可用路径",
            TargetNavigationFailureCode.TeleportUnavailable => "传送点不可用",
            TargetNavigationFailureCode.CoordinateConversionFailed => "规划坐标转换失败",
            TargetNavigationFailureCode.PlannedTaskInvalid => "规划路线没有足够的可执行点",
            TargetNavigationFailureCode.CaptureNotInitialized => "截图器未初始化",
            TargetNavigationFailureCode.GameWindowNotFound => "原神窗口不存在",
            TargetNavigationFailureCode.NotInMainUi => "当前不在主界面",
            TargetNavigationFailureCode.TaskRunnerBusy => "其他独立任务正在运行",
            TargetNavigationFailureCode.WindowActivationFailed => "原神窗口激活失败",
            TargetNavigationFailureCode.GameWindowLostFocus => "原神窗口已失去前台",
            TargetNavigationFailureCode.ExecutionFailed => "路线执行失败",
            TargetNavigationFailureCode.UserCancelled => "用户取消",
            _ => "目标导航发生未知错误"
        };

        return string.IsNullOrWhiteSpace(detail) || string.Equals(message, detail, StringComparison.Ordinal)
            ? message
            : $"{message}：{detail}";
    }
}

public sealed class TargetNavigationRequest
{
    public string MapName { get; init; } = "Teyvat";

    public string? MapMatchMethod { get; init; }

    public RouteGraphPoint TargetImagePoint { get; init; }

    public RouteGraphPoint? LastKnownCurrentImagePoint { get; init; }

    public string TaskName { get; init; } = "地图目标导航";

    public string? TargetMoveMode { get; init; }

    public string? TargetAction { get; init; }

    public string? TargetActionParams { get; init; }

    public string? TargetResourceId { get; init; }

    public string? TargetResourceLabelId { get; init; }

    public string? TargetResourceName { get; init; }

    public RouteNavigationPlanOptions Options { get; init; } = new();

    public bool ForceReplan { get; init; }

    public bool Execute { get; init; } = true;

    public RouteNavigationPlanRequest BuildPlanRequest(RouteGraphPoint currentImagePoint)
    {
        return new RouteNavigationPlanRequest
        {
            MapName = MapName,
            MapMatchMethod = MapMatchMethod,
            CurrentImagePoint = currentImagePoint,
            TargetImagePoint = TargetImagePoint,
            TaskName = TaskName,
            TargetMoveMode = TargetMoveMode,
            TargetAction = TargetAction,
            TargetActionParams = TargetActionParams,
            TargetResourceId = TargetResourceId,
            TargetResourceLabelId = TargetResourceLabelId,
            TargetResourceName = TargetResourceName
        };
    }
}

public sealed record TargetNavigationFailure(TargetNavigationFailureCode Code, string Message)
{
    public static TargetNavigationFailure Create(TargetNavigationFailureCode code, string? detail = null)
    {
        return new TargetNavigationFailure(code, TargetNavigationFailureMessages.Format(code, detail));
    }
}

public sealed record TargetNavigationStatus(
    TargetNavigationState State,
    string Text,
    TargetNavigationFailure? Failure = null);

public sealed class TargetNavigationPreparationResult
{
    public bool Succeeded { get; private init; }

    public string ActualMapName { get; private init; } = string.Empty;

    public RouteGraphPoint CurrentImagePoint { get; private init; }

    public TargetNavigationFailure? Failure { get; private init; }

    public static TargetNavigationPreparationResult Ready(string actualMapName, RouteGraphPoint currentImagePoint)
    {
        return new TargetNavigationPreparationResult
        {
            Succeeded = true,
            ActualMapName = actualMapName,
            CurrentImagePoint = currentImagePoint
        };
    }

    public static TargetNavigationPreparationResult Failed(
        TargetNavigationFailureCode code,
        string? detail = null)
    {
        return new TargetNavigationPreparationResult
        {
            Failure = TargetNavigationFailure.Create(code, detail)
        };
    }
}

public sealed class TargetNavigationExecutionResult
{
    public bool Succeeded { get; private init; }

    public bool Cancelled { get; private init; }

    public TargetNavigationFailure? Failure { get; private init; }

    public static TargetNavigationExecutionResult Completed()
    {
        return new TargetNavigationExecutionResult { Succeeded = true };
    }

    public static TargetNavigationExecutionResult Failed(
        TargetNavigationFailureCode code,
        string? detail = null)
    {
        return new TargetNavigationExecutionResult
        {
            Failure = TargetNavigationFailure.Create(code, detail)
        };
    }

    public static TargetNavigationExecutionResult CancelledByUser()
    {
        return new TargetNavigationExecutionResult
        {
            Cancelled = true,
            Failure = TargetNavigationFailure.Create(TargetNavigationFailureCode.UserCancelled)
        };
    }
}

public sealed class TargetNavigationRunResult
{
    public bool Succeeded { get; init; }

    public bool ReusedExistingPlan { get; init; }

    public TargetNavigationState FinalState { get; init; }

    public TargetNavigationFailure? Failure { get; init; }

    public RouteNavigationPlan? Plan { get; init; }

    public PathingTask? ExecutedTask { get; init; }
}

public interface ITargetNavigationPlanningRuntime
{
    Task<TargetNavigationPreparationResult> ResolvePlanningPositionAsync(
        string expectedMapName,
        string? mapMatchMethod,
        CancellationToken cancellationToken);
}

public interface ITargetNavigationExecutionRuntime
{
    Task<TargetNavigationPreparationResult> WaitUntilReadyAsync(
        string expectedMapName,
        string? mapMatchMethod,
        RouteNavigationCostOptions costOptions,
        CancellationToken cancellationToken);

    Task<TargetNavigationExecutionResult> ExecuteAsync(
        PathingTask task,
        CancellationToken cancellationToken);

    void ReleaseAllInputs();
}

public interface ITargetNavigationRuntime :
    ITargetNavigationPlanningRuntime,
    ITargetNavigationExecutionRuntime
{
}
