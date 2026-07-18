using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.TargetNavigation;

public sealed class TargetNavigationWorkflow(
    IRouteNavigationPlanner planner,
    ITargetNavigationRuntime runtime,
    ILocalTargetNavigator? localTargetNavigator = null,
    IRouteCoordinateConverter? coordinateConverter = null)
{
    public async Task<TargetNavigationRunResult> RunAsync(
        TargetNavigationRequest request,
        RouteNavigationPlan? existingPlan = null,
        Action<TargetNavigationStatus>? onStatusChanged = null,
        Action<RouteNavigationPlan>? onPlanReady = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Publish(TargetNavigationState.WaitingToStart, "等待启动", onStatusChanged);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preparation = request.LastKnownCurrentImagePoint is { } lastKnownCurrent
                ? TargetNavigationPreparationResult.Ready(request.MapName, lastKnownCurrent)
                : await runtime.ResolvePlanningPositionAsync(
                    request.MapName,
                    request.MapMatchMethod,
                    cancellationToken);
            if (!preparation.Succeeded)
            {
                var failure = preparation.Failure ??
                              TargetNavigationFailure.Create(TargetNavigationFailureCode.Unexpected);
                return Fail(
                    ResolvePreparationFailureState(failure.Code),
                    failure,
                    onStatusChanged);
            }

            if (!SameMap(request.MapName, preparation.ActualMapName))
            {
                return Fail(
                    TargetNavigationState.PlanFailed,
                    TargetNavigationFailure.Create(
                        TargetNavigationFailureCode.MapMismatch,
                        $"当前 {preparation.ActualMapName}，目标 {request.MapName}"),
                    onStatusChanged);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var reused = !request.ForceReplan &&
                         RouteNavigationPlanReusePolicy.CanReuse(
                             existingPlan,
                             request,
                             preparation.CurrentImagePoint);
            RouteNavigationPlan plan;
            if (reused)
            {
                plan = existingPlan!;
            }
            else
            {
                Publish(TargetNavigationState.Planning, "正在规划", onStatusChanged);
                var planRequest = request.BuildPlanRequest(preparation.CurrentImagePoint);
                var planned = await Task.Run(
                    () =>
                    {
                        var succeeded = planner.TryPlan(planRequest, out var result, request.Options);
                        return (succeeded, result);
                    },
                    cancellationToken);
                plan = planned.result;
                if (!planned.succeeded || !plan.Succeeded)
                {
                    return Fail(
                        TargetNavigationState.PlanFailed,
                        CreatePlanningFailure(plan),
                        onStatusChanged,
                        plan);
                }
            }

            PathingTask? task = plan.Task;
            if (plan.CompletionMode != RoutePlanCompletionMode.LocalOnly &&
                task is not { Positions.Count: >= 2 })
            {
                return Fail(
                    TargetNavigationState.PlanFailed,
                    TargetNavigationFailure.Create(TargetNavigationFailureCode.PlannedTaskInvalid),
                    onStatusChanged,
                    plan);
            }

            onPlanReady?.Invoke(plan);
            Publish(TargetNavigationState.PlanSucceeded, "规划成功", onStatusChanged);

            if (!request.Execute)
            {
                return new TargetNavigationRunResult
                {
                    Succeeded = true,
                    ReusedExistingPlan = reused,
                    FinalState = TargetNavigationState.PlanSucceeded,
                    Plan = plan
                };
            }

            Publish(TargetNavigationState.WaitingToStart, "等待启动", onStatusChanged);
            cancellationToken.ThrowIfCancellationRequested();
            var readiness = await runtime.WaitUntilReadyAsync(
                request.MapName,
                request.MapMatchMethod,
                request.Options.CostOptions,
                cancellationToken);
            if (!readiness.Succeeded)
            {
                var failure = readiness.Failure ??
                              TargetNavigationFailure.Create(TargetNavigationFailureCode.Unexpected);
                return Fail(
                    ResolvePreparationFailureState(failure.Code),
                    failure,
                    onStatusChanged,
                    plan,
                    task,
                    reused);
            }

            if (!SameMap(request.MapName, readiness.ActualMapName))
            {
                return Fail(
                    TargetNavigationState.ExecutionFailed,
                    TargetNavigationFailure.Create(
                        TargetNavigationFailureCode.MapMismatch,
                        $"执行前定位为 {readiness.ActualMapName}，目标 {request.MapName}"),
                    onStatusChanged,
                    plan,
                    task,
                    reused);
            }

            var converterForDrift = coordinateConverter ?? RouteNavigationCoordinateService.Instance;
            if (!TryMeasureGameDistance(
                    converterForDrift,
                    request.MapName,
                    request.MapMatchMethod,
                    preparation.CurrentImagePoint,
                    readiness.CurrentImagePoint,
                    out var driftDistance))
            {
                return Fail(
                    TargetNavigationState.ExecutionFailed,
                    TargetNavigationFailure.Create(TargetNavigationFailureCode.CoordinateConversionFailed),
                    onStatusChanged,
                    plan,
                    task,
                    reused);
            }

            if (driftDistance > request.Options.CostOptions.ReplanDriftGameDistance)
            {
                Publish(TargetNavigationState.Planning, $"位置漂移 {driftDistance:F1}，重新规划", onStatusChanged);
                var replannedRequest = request.BuildPlanRequest(readiness.CurrentImagePoint);
                var replanned = await Task.Run(
                    () =>
                    {
                        var succeeded = planner.TryPlan(replannedRequest, out var result, request.Options);
                        return (succeeded, result);
                    },
                    cancellationToken);
                if (!replanned.succeeded || !replanned.result.Succeeded)
                {
                    return Fail(
                        TargetNavigationState.PlanFailed,
                        CreatePlanningFailure(replanned.result),
                        onStatusChanged,
                        replanned.result);
                }

                var replannedTask = replanned.result.Task;
                if (replanned.result.CompletionMode != RoutePlanCompletionMode.LocalOnly &&
                    replannedTask is not { Positions.Count: >= 2 })
                {
                    return Fail(
                        TargetNavigationState.PlanFailed,
                        TargetNavigationFailure.Create(TargetNavigationFailureCode.PlannedTaskInvalid),
                        onStatusChanged,
                        replanned.result);
                }

                plan = replanned.result;
                task = replannedTask;
                reused = false;
                onPlanReady?.Invoke(plan);
                Publish(TargetNavigationState.PlanSucceeded, "重新规划成功", onStatusChanged);
            }

            Publish(TargetNavigationState.Executing, "正在执行", onStatusChanged);
            if (plan.CompletionMode != RoutePlanCompletionMode.LocalOnly)
            {
                var execution = await runtime.ExecuteAsync(task!, cancellationToken);
                if (execution.Cancelled)
                {
                    return Fail(
                        TargetNavigationState.UserCancelled,
                        execution.Failure ?? TargetNavigationFailure.Create(TargetNavigationFailureCode.UserCancelled),
                        onStatusChanged,
                        plan,
                        task,
                        reused);
                }

                if (!execution.Succeeded)
                {
                    return Fail(
                        TargetNavigationState.ExecutionFailed,
                        execution.Failure ?? TargetNavigationFailure.Create(TargetNavigationFailureCode.ExecutionFailed),
                        onStatusChanged,
                        plan,
                        task,
                        reused);
                }
            }

            if (plan.CompletionMode is RoutePlanCompletionMode.PartialToFrontier or RoutePlanCompletionMode.LocalOnly)
            {
                if (localTargetNavigator == null || plan.FrontierNode == null)
                {
                    return Fail(
                        TargetNavigationState.ExecutionFailed,
                        TargetNavigationFailure.Create(
                            TargetNavigationFailureCode.ExecutionFailed,
                            "部分路线已到达前沿，但局部导航器不可用"),
                        onStatusChanged,
                        plan,
                        task,
                        reused);
                }

                var converter = coordinateConverter ?? RouteNavigationCoordinateService.Instance;
                var frontierPoint = new RouteGraphPoint(plan.FrontierNode.X, plan.FrontierNode.Y);
                if (!converter.TryImageToGame(
                        request.MapName,
                        request.MapMatchMethod,
                        frontierPoint,
                        out var frontierGamePoint) ||
                    !converter.TryImageToGame(
                        request.MapName,
                        request.MapMatchMethod,
                        request.TargetImagePoint,
                        out var targetGamePoint))
                {
                    return Fail(
                        TargetNavigationState.ExecutionFailed,
                        TargetNavigationFailure.Create(TargetNavigationFailureCode.CoordinateConversionFailed),
                        onStatusChanged,
                        plan,
                        task,
                        reused);
                }

                var localResult = await localTargetNavigator.NavigateAsync(
                    new LocalTargetNavigationRequest
                    {
                        MapName = request.MapName,
                        MapMatchMethod = request.MapMatchMethod,
                        TargetImagePoint = request.TargetImagePoint,
                        RemainingGameDistance = Distance(frontierGamePoint, targetGamePoint),
                        Options = request.Options.CostOptions
                    },
                    cancellationToken);
                if (!localResult.Succeeded)
                {
                    var cancelled = localResult.FailureCode == LocalNavigationFailureCode.Cancelled;
                    return Fail(
                        cancelled ? TargetNavigationState.UserCancelled : TargetNavigationState.ExecutionFailed,
                        TargetNavigationFailure.Create(
                            cancelled ? TargetNavigationFailureCode.UserCancelled : TargetNavigationFailureCode.ExecutionFailed,
                            localResult.Detail ?? localResult.FailureCode.ToString()),
                        onStatusChanged,
                        plan,
                        task,
                        reused);
                }
            }

            Publish(TargetNavigationState.Completed, "执行完成", onStatusChanged);
            return new TargetNavigationRunResult
            {
                Succeeded = true,
                ReusedExistingPlan = reused,
                FinalState = TargetNavigationState.Completed,
                Plan = plan,
                ExecutedTask = task
            };
        }
        catch (OperationCanceledException)
        {
            return Fail(
                TargetNavigationState.UserCancelled,
                TargetNavigationFailure.Create(TargetNavigationFailureCode.UserCancelled),
                onStatusChanged);
        }
        catch (Exception ex)
        {
            return Fail(
                TargetNavigationState.ExecutionFailed,
                TargetNavigationFailure.Create(TargetNavigationFailureCode.Unexpected, ex.Message),
                onStatusChanged);
        }
        finally
        {
            try
            {
                runtime.ReleaseAllInputs();
            }
            catch
            {
                // 安全释放不能覆盖原始导航结果。
            }
        }
    }

    private static TargetNavigationRunResult Fail(
        TargetNavigationState state,
        TargetNavigationFailure failure,
        Action<TargetNavigationStatus>? onStatusChanged,
        RouteNavigationPlan? plan = null,
        PathingTask? executedTask = null,
        bool reused = false)
    {
        Publish(state, failure.Message, onStatusChanged, failure);
        return new TargetNavigationRunResult
        {
            Succeeded = false,
            ReusedExistingPlan = reused,
            FinalState = state,
            Failure = failure,
            Plan = plan,
            ExecutedTask = executedTask
        };
    }

    private static TargetNavigationFailure CreatePlanningFailure(RouteNavigationPlan plan)
    {
        var code = plan.FailureCode switch
        {
            RouteNavigationFailureCode.GraphFileMissing => TargetNavigationFailureCode.GraphFileMissing,
            RouteNavigationFailureCode.GraphEmpty => TargetNavigationFailureCode.GraphEmpty,
            RouteNavigationFailureCode.GraphInvalid => TargetNavigationFailureCode.GraphInvalid,
            RouteNavigationFailureCode.CurrentPointNotConnected => TargetNavigationFailureCode.CurrentPointNotConnected,
            RouteNavigationFailureCode.TargetPointNotConnected => TargetNavigationFailureCode.TargetPointNotConnected,
            RouteNavigationFailureCode.NoRoute => TargetNavigationFailureCode.NoRoute,
            RouteNavigationFailureCode.TeleportUnavailable => TargetNavigationFailureCode.TeleportUnavailable,
            RouteNavigationFailureCode.CoordinateConversionFailed => TargetNavigationFailureCode.CoordinateConversionFailed,
            RouteNavigationFailureCode.PlannedTaskInvalid => TargetNavigationFailureCode.PlannedTaskInvalid,
            _ => TargetNavigationFailureCode.Unexpected
        };
        return TargetNavigationFailure.Create(code, plan.FailureReason);
    }

    private static TargetNavigationState ResolvePreparationFailureState(TargetNavigationFailureCode code)
    {
        return code switch
        {
            TargetNavigationFailureCode.GraphFileMissing or
            TargetNavigationFailureCode.GraphEmpty or
            TargetNavigationFailureCode.GraphInvalid or
            TargetNavigationFailureCode.GraphNotLoaded or
            TargetNavigationFailureCode.CurrentPositionUnrecognized or
            TargetNavigationFailureCode.CurrentPointNotConnected or
            TargetNavigationFailureCode.TargetPointNotConnected or
            TargetNavigationFailureCode.MapMismatch or
            TargetNavigationFailureCode.NoRoute or
            TargetNavigationFailureCode.TeleportUnavailable or
            TargetNavigationFailureCode.CoordinateConversionFailed or
            TargetNavigationFailureCode.PlannedTaskInvalid => TargetNavigationState.PlanFailed,
            _ => TargetNavigationState.ExecutionFailed
        };
    }

    private static void Publish(
        TargetNavigationState state,
        string text,
        Action<TargetNavigationStatus>? onStatusChanged,
        TargetNavigationFailure? failure = null)
    {
        onStatusChanged?.Invoke(new TargetNavigationStatus(state, text, failure));
    }

    private static bool SameMap(string expected, string actual)
    {
        return string.Equals(
            RouteGraphGeometry.NormalizeMapName(expected),
            RouteGraphGeometry.NormalizeMapName(actual),
            StringComparison.OrdinalIgnoreCase);
    }

    private static double Distance(RouteGamePoint from, RouteGamePoint to)
    {
        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool TryMeasureGameDistance(
        IRouteCoordinateConverter converter,
        string mapName,
        string? mapMatchMethod,
        RouteGraphPoint from,
        RouteGraphPoint to,
        out double distance)
    {
        distance = 0;
        if (RouteGraphGeometry.Distance(from, to) <= 0.0001)
        {
            return true;
        }

        if (!converter.TryImageToGame(mapName, mapMatchMethod, from, out var fromGame) ||
            !converter.TryImageToGame(mapName, mapMatchMethod, to, out var toGame))
        {
            return false;
        }

        distance = Distance(fromGame, toGame);
        return true;
    }
}

internal static class RouteNavigationPlanReusePolicy
{
    public static bool CanReuse(
        RouteNavigationPlan? plan,
        TargetNavigationRequest request,
        RouteGraphPoint currentImagePoint)
    {
        if (plan is not { Succeeded: true, Task.Positions.Count: >= 2, Request: not null })
        {
            return false;
        }

        var plannedRequest = plan.Request;
        if (!string.Equals(
                RouteGraphGeometry.NormalizeMapName(plannedRequest.MapName),
                RouteGraphGeometry.NormalizeMapName(request.MapName),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plannedRequest.MapMatchMethod, request.MapMatchMethod, StringComparison.OrdinalIgnoreCase) ||
            RouteGraphGeometry.Distance(plannedRequest.TargetImagePoint, request.TargetImagePoint) > 0.01 ||
            !string.Equals(plannedRequest.TargetMoveMode, request.TargetMoveMode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plannedRequest.TargetAction, request.TargetAction, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plannedRequest.TargetActionParams, request.TargetActionParams, StringComparison.Ordinal) ||
            !string.Equals(plannedRequest.TargetResourceId, request.TargetResourceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plannedRequest.TargetResourceLabelId, request.TargetResourceLabelId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plannedRequest.TargetResourceName, request.TargetResourceName, StringComparison.OrdinalIgnoreCase) ||
            !SameOptions(plan.Options, request.Options))
        {
            return false;
        }

        var maximumStartDrift = Math.Max(10, request.Options.CurrentAttachMaxDistance);
        return RouteGraphGeometry.Distance(plannedRequest.CurrentImagePoint, currentImagePoint) <= maximumStartDrift;
    }

    private static bool SameOptions(RouteNavigationPlanOptions? left, RouteNavigationPlanOptions right)
    {
        return left != null &&
               left.AllowTeleport == right.AllowTeleport &&
               left.AllowDisabledEdges == right.AllowDisabledEdges &&
               left.AllowUnknownStartConnector == right.AllowUnknownStartConnector &&
               left.AllowUnknownTargetConnector == right.AllowUnknownTargetConnector &&
               left.CurrentNodeCandidateLimit == right.CurrentNodeCandidateLimit &&
               left.TargetNodeCandidateLimit == right.TargetNodeCandidateLimit &&
               left.TeleportCandidateLimit == right.TeleportCandidateLimit &&
               left.MaxStartCandidates == right.MaxStartCandidates &&
               left.CurrentAttachMaxDistance.Equals(right.CurrentAttachMaxDistance) &&
               left.TargetAttachMaxDistance.Equals(right.TargetAttachMaxDistance) &&
               left.UnknownConnectorMaxDistance.Equals(right.UnknownConnectorMaxDistance) &&
               left.TeleportSearchMaxDistance.Equals(right.TeleportSearchMaxDistance) &&
               left.CurrentAttachCostWeight.Equals(right.CurrentAttachCostWeight) &&
               left.TargetAttachCostWeight.Equals(right.TargetAttachCostWeight) &&
               left.UnknownConnectorCostWeight.Equals(right.UnknownConnectorCostWeight) &&
               SameCostOptions(left.CostOptions, right.CostOptions) &&
               left.OutputPointMinDistance.Equals(right.OutputPointMinDistance) &&
               left.TargetOutputMinDistance.Equals(right.TargetOutputMinDistance) &&
               left.ResourceSemanticMaxDistance.Equals(right.ResourceSemanticMaxDistance) &&
               left.ResourceSemanticAttachCostMultiplier.Equals(right.ResourceSemanticAttachCostMultiplier) &&
               left.FrontierRemainingTimeWeight.Equals(right.FrontierRemainingTimeWeight);
    }

    private static bool SameCostOptions(RouteNavigationCostOptions left, RouteNavigationCostOptions right)
    {
        return left.WalkSpeed.Equals(right.WalkSpeed) &&
               left.RunSpeed.Equals(right.RunSpeed) &&
               left.DashSpeed.Equals(right.DashSpeed) &&
               left.SwimSpeed.Equals(right.SwimSpeed) &&
               left.FlySpeed.Equals(right.FlySpeed) &&
               left.ClimbSpeed.Equals(right.ClimbSpeed) &&
               left.JumpSpeed.Equals(right.JumpSpeed) &&
               left.TeleportDurationSeconds.Equals(right.TeleportDurationSeconds) &&
               left.MinimumTeleportSavingsSeconds.Equals(right.MinimumTeleportSavingsSeconds) &&
               left.LocalDirectMaxGameDistance.Equals(right.LocalDirectMaxGameDistance) &&
               left.ReplanDriftGameDistance.Equals(right.ReplanDriftGameDistance) &&
               left.TalkWaitTimeoutSeconds.Equals(right.TalkWaitTimeoutSeconds) &&
               left.LocalIconMissRetryCount == right.LocalIconMissRetryCount &&
               left.LocalFollowTimeoutSeconds.Equals(right.LocalFollowTimeoutSeconds) &&
               left.LocalRecognitionRetryDelayMilliseconds == right.LocalRecognitionRetryDelayMilliseconds &&
               left.LocalForwardStepMilliseconds == right.LocalForwardStepMilliseconds &&
               left.LocalJumpIntervalMilliseconds == right.LocalJumpIntervalMilliseconds &&
               left.LocalSettleMilliseconds == right.LocalSettleMilliseconds &&
               left.LocalArrivalGameDistance.Equals(right.LocalArrivalGameDistance) &&
               left.LocalTemplateThreshold.Equals(right.LocalTemplateThreshold) &&
               left.LocalIconCenterX.Equals(right.LocalIconCenterX) &&
               left.LocalIconCenterTolerance.Equals(right.LocalIconCenterTolerance) &&
               left.LocalIconMaximumY.Equals(right.LocalIconMaximumY) &&
               left.LocalMouseAdjustmentUnit == right.LocalMouseAdjustmentUnit &&
               left.LocalVerticalMouseAdjustment == right.LocalVerticalMouseAdjustment;
    }
}
