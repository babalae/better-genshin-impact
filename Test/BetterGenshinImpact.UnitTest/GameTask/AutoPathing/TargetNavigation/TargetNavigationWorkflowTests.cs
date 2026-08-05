using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoPathing.TargetNavigation;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

namespace BetterGenshinImpact.UnitTest.AutoPathing.TargetNavigation;

public class TargetNavigationWorkflowTests
{
    public TargetNavigationWorkflowTests()
    {
        TestConfigEnvironment.EnsureInitialized();
    }

    [Fact]
    public async Task RunAsync_PlansPublishesAndExecutesTheSameTaskInOrder()
    {
        var events = new List<string>();
        var task = CreateTask();
        var plan = new RouteNavigationPlan { Succeeded = true, Task = task };
        var planner = new FakePlanner(plan, events);
        var runtime = new FakeRuntime(events)
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(10, 20)),
            Execution = TargetNavigationExecutionResult.Completed()
        };
        var workflow = new TargetNavigationWorkflow(planner, runtime);

        var result = await workflow.RunAsync(
            CreateRequest(),
            onStatusChanged: status => events.Add($"status:{status.State}"),
            onPlanReady: readyPlan =>
            {
                Assert.Same(plan, readyPlan);
                events.Add("plan-ready");
            });

        Assert.True(result.Succeeded);
        Assert.Same(task, result.ExecutedTask);
        Assert.Same(task, runtime.ExecutedTask);
        Assert.Equal(
        [
            "status:WaitingToStart",
            "planning-position",
            "status:Planning",
            "plan",
            "plan-ready",
            "status:PlanSucceeded",
            "status:WaitingToStart",
            "wait-ready",
            "status:Executing",
            "execute",
            "planning-position",
            "status:Completed",
            "release"
        ], events);
    }

    [Fact]
    public async Task RunAsync_ReusesAValidPlanWithoutCallingPlanner()
    {
        var request = CreateRequest();
        var current = new RouteGraphPoint(10, 20);
        var task = CreateTask();
        var existingPlan = CreateReusablePlan(request, current, task);
        var planner = new FakePlanner(RouteNavigationPlan.Failed(RouteNavigationFailureCode.NoRoute, "should not plan"));
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", current),
            Execution = TargetNavigationExecutionResult.Completed()
        };

        var result = await new TargetNavigationWorkflow(planner, runtime)
            .RunAsync(request, existingPlan);

        Assert.True(result.Succeeded);
        Assert.True(result.ReusedExistingPlan);
        Assert.Equal(0, planner.CallCount);
        Assert.Same(task, runtime.ExecutedTask);
    }

    [Fact]
    public async Task RunAsync_TargetChangeInvalidatesExistingPlanAndReplans()
    {
        var oldRequest = CreateRequest();
        var newRequest = CreateRequest(new RouteGraphPoint(500, 600));
        var current = new RouteGraphPoint(10, 20);
        var replacementTask = CreateTask();
        var planner = new FakePlanner(new RouteNavigationPlan { Succeeded = true, Task = replacementTask });
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", current),
            ArrivalPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(500, 600)),
            Execution = TargetNavigationExecutionResult.Completed()
        };

        var result = await new TargetNavigationWorkflow(planner, runtime)
            .RunAsync(newRequest, CreateReusablePlan(oldRequest, current, CreateTask()));

        Assert.True(result.Succeeded);
        Assert.False(result.ReusedExistingPlan);
        Assert.Equal(1, planner.CallCount);
        Assert.Same(replacementTask, runtime.ExecutedTask);
    }

    [Fact]
    public async Task RunAsync_OptionChangeInvalidatesExistingPlanAndReplans()
    {
        var oldRequest = CreateRequest();
        var newRequest = CreateRequest(allowTeleport: false);
        var current = new RouteGraphPoint(10, 20);
        var replacementTask = CreateTask();
        var planner = new FakePlanner(new RouteNavigationPlan { Succeeded = true, Task = replacementTask });
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", current),
            Execution = TargetNavigationExecutionResult.Completed()
        };

        var result = await new TargetNavigationWorkflow(planner, runtime)
            .RunAsync(newRequest, CreateReusablePlan(oldRequest, current, CreateTask()));

        Assert.True(result.Succeeded);
        Assert.False(result.ReusedExistingPlan);
        Assert.Equal(1, planner.CallCount);
        Assert.Same(replacementTask, runtime.ExecutedTask);
    }

    [Fact]
    public async Task RunAsync_PlanningFailureDoesNotExecuteAndAlwaysReleasesInputs()
    {
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(10, 20))
        };
        var planner = new FakePlanner(RouteNavigationPlan.Failed(RouteNavigationFailureCode.NoRoute, "no connected route found"));

        var result = await new TargetNavigationWorkflow(planner, runtime).RunAsync(CreateRequest());

        Assert.False(result.Succeeded);
        Assert.Equal(TargetNavigationState.PlanFailed, result.FinalState);
        Assert.Equal(TargetNavigationFailureCode.NoRoute, result.Failure?.Code);
        Assert.Null(runtime.ExecutedTask);
        Assert.Equal(1, runtime.ReleaseCount);
    }

    [Theory]
    [InlineData(TargetNavigationFailureCode.GraphFileMissing, TargetNavigationState.PlanFailed)]
    [InlineData(TargetNavigationFailureCode.GraphEmpty, TargetNavigationState.PlanFailed)]
    [InlineData(TargetNavigationFailureCode.CaptureNotInitialized, TargetNavigationState.ExecutionFailed)]
    [InlineData(TargetNavigationFailureCode.TaskRunnerBusy, TargetNavigationState.ExecutionFailed)]
    public async Task RunAsync_PreparationFailureUsesTheCorrectState(
        TargetNavigationFailureCode failureCode,
        TargetNavigationState expectedState)
    {
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Failed(failureCode)
        };
        var planner = new FakePlanner(new RouteNavigationPlan { Succeeded = true, Task = CreateTask() });

        var result = await new TargetNavigationWorkflow(planner, runtime).RunAsync(CreateRequest());

        Assert.Equal(expectedState, result.FinalState);
        Assert.Equal(failureCode, result.Failure?.Code);
        Assert.Equal(0, planner.CallCount);
        Assert.Null(runtime.ExecutedTask);
        Assert.Equal(1, runtime.ReleaseCount);
    }

    [Fact]
    public async Task RunAsync_MapMismatchStopsBeforePlanning()
    {
        var planner = new FakePlanner(new RouteNavigationPlan { Succeeded = true, Task = CreateTask() });
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Enkanomiya", new RouteGraphPoint(10, 20))
        };

        var result = await new TargetNavigationWorkflow(planner, runtime).RunAsync(CreateRequest());

        Assert.Equal(TargetNavigationFailureCode.MapMismatch, result.Failure?.Code);
        Assert.Equal(0, planner.CallCount);
        Assert.Null(runtime.ExecutedTask);
        Assert.Equal(1, runtime.ReleaseCount);
    }

    [Fact]
    public async Task RunAsync_ExecutionFailureIsReportedAndInputsAreReleased()
    {
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(10, 20)),
            Execution = TargetNavigationExecutionResult.Failed(TargetNavigationFailureCode.ExecutionFailed, "PathExecutor failed")
        };
        var planner = new FakePlanner(new RouteNavigationPlan { Succeeded = true, Task = CreateTask() });

        var result = await new TargetNavigationWorkflow(planner, runtime).RunAsync(CreateRequest());

        Assert.Equal(TargetNavigationState.ExecutionFailed, result.FinalState);
        Assert.Equal(TargetNavigationFailureCode.ExecutionFailed, result.Failure?.Code);
        Assert.Equal(1, runtime.ReleaseCount);
    }

    [Fact]
    public async Task RunAsync_CancellationReportsUserCancelledAndReleasesInputs()
    {
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(10, 20)),
            Execution = TargetNavigationExecutionResult.CancelledByUser()
        };
        var planner = new FakePlanner(new RouteNavigationPlan { Succeeded = true, Task = CreateTask() });

        var result = await new TargetNavigationWorkflow(planner, runtime).RunAsync(CreateRequest());

        Assert.Equal(TargetNavigationState.UserCancelled, result.FinalState);
        Assert.Equal(TargetNavigationFailureCode.UserCancelled, result.Failure?.Code);
        Assert.Equal(1, runtime.ReleaseCount);
    }

    [Fact]
    public async Task RunAsync_ExecutesPartialPlanThenInvokesLocalNavigator()
    {
        var task = CreateTask();
        var plan = new RouteNavigationPlan
        {
            Succeeded = true,
            CompletionMode = RoutePlanCompletionMode.PartialToFrontier,
            Task = task,
            FrontierNode = new RouteNavigationNode { NodeId = "frontier", MapName = "Teyvat", X = 40, Y = 0 },
            TargetImagePoint = new RouteGraphPoint(100, 0)
        };
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            ArrivalPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(100, 0)),
            Execution = TargetNavigationExecutionResult.Completed()
        };
        var localNavigator = new FakeLocalNavigator();
        var workflow = new TargetNavigationWorkflow(
            new FakePlanner(plan),
            runtime,
            localNavigator,
            new IdentityCoordinateConverter());

        var result = await workflow.RunAsync(CreateRequest(new RouteGraphPoint(100, 0)));

        Assert.True(result.Succeeded);
        Assert.Same(task, runtime.ExecutedTask);
        Assert.NotNull(localNavigator.Request);
        Assert.Equal(60, localNavigator.Request!.RemainingGameDistance, precision: 2);
        Assert.Equal(1, localNavigator.CallCount);
    }

    [Fact]
    public async Task RunAsync_PlansBeforeWaitingForMainUi()
    {
        var events = new List<string>();
        var runtime = new FakeRuntime(events)
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(10, 20)),
            ExecutionPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(10, 20)),
            Execution = TargetNavigationExecutionResult.Completed()
        };
        var planner = new FakePlanner(new RouteNavigationPlan { Succeeded = true, Task = CreateTask() }, events);

        var result = await new TargetNavigationWorkflow(planner, runtime).RunAsync(CreateRequest());

        Assert.True(result.Succeeded);
        Assert.True(events.IndexOf("plan") < events.IndexOf("wait-ready"));
    }

    [Fact]
    public async Task RunAsync_ReplansWhenPositionDriftsAfterExecutionReadiness()
    {
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            ExecutionPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(25, 0)),
            Execution = TargetNavigationExecutionResult.Completed()
        };
        var planner = new FakePlanner(new RouteNavigationPlan { Succeeded = true, Task = CreateTask() });

        var result = await new TargetNavigationWorkflow(
                planner,
                runtime,
                coordinateConverter: new IdentityCoordinateConverter())
            .RunAsync(CreateRequest());

        Assert.True(result.Succeeded);
        Assert.Equal(2, planner.CallCount);
    }

    [Fact]
    public async Task RunAsync_UsesSuppliedLastKnownPositionWithoutRequiringVisibleMiniMap()
    {
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Failed(TargetNavigationFailureCode.CurrentPositionUnrecognized),
            ExecutionPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(10, 20)),
            Execution = TargetNavigationExecutionResult.Completed()
        };
        var planner = new FakePlanner(new RouteNavigationPlan { Succeeded = true, Task = CreateTask() });

        var result = await new TargetNavigationWorkflow(planner, runtime)
            .RunAsync(CreateRequest(lastKnownCurrent: new RouteGraphPoint(10, 20)));

        Assert.True(result.Succeeded);
        Assert.Equal(1, runtime.PlanningPositionCallCount);
        Assert.Equal(1, planner.CallCount);
    }

    [Fact]
    public async Task RunAsync_PreviewsFromTeleportWhenCurrentPositionIsUnavailable()
    {
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Failed(
                TargetNavigationFailureCode.CurrentPositionUnrecognized)
        };
        var planner = new FakePlanner(new RouteNavigationPlan { Succeeded = true, Task = CreateTask() });

        var result = await new TargetNavigationWorkflow(planner, runtime)
            .RunAsync(CreateRequest(execute: false));

        Assert.True(result.Succeeded);
        Assert.Equal(1, planner.CallCount);
        Assert.False(Assert.Single(planner.Requests).HasCurrentPosition);
        Assert.Null(runtime.ExecutedTask);
    }

    [Fact]
    public async Task RunAsync_ReplansFromActualPositionBeforeExecutingOfflinePreview()
    {
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Failed(
                TargetNavigationFailureCode.CurrentPositionUnrecognized),
            ExecutionPreparation = TargetNavigationPreparationResult.Ready(
                "Teyvat",
                new RouteGraphPoint(25, 35)),
            Execution = TargetNavigationExecutionResult.Completed()
        };
        var planner = new FakePlanner(new RouteNavigationPlan { Succeeded = true, Task = CreateTask() });

        var result = await new TargetNavigationWorkflow(planner, runtime)
            .RunAsync(CreateRequest());

        Assert.True(result.Succeeded);
        Assert.Equal(2, planner.CallCount);
        Assert.False(planner.Requests[0].HasCurrentPosition);
        Assert.True(planner.Requests[1].HasCurrentPosition);
        Assert.Equal(new RouteGraphPoint(25, 35), planner.Requests[1].CurrentImagePoint);
        Assert.NotNull(runtime.ExecutedTask);
    }

    [Fact]
    public async Task RunAsync_LocalOnlyPlanSkipsPathExecutorAndInvokesLocalNavigator()
    {
        var plan = new RouteNavigationPlan
        {
            Succeeded = true,
            CompletionMode = RoutePlanCompletionMode.LocalOnly,
            FrontierNode = new RouteNavigationNode { NodeId = "current", MapName = "Teyvat", X = 0, Y = 0 },
            TargetImagePoint = new RouteGraphPoint(50, 0)
        };
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            ExecutionPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            ArrivalPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(50, 0))
        };
        var localNavigator = new FakeLocalNavigator();

        var result = await new TargetNavigationWorkflow(
                new FakePlanner(plan),
                runtime,
                localNavigator,
                new IdentityCoordinateConverter())
            .RunAsync(CreateRequest(new RouteGraphPoint(50, 0)));

        Assert.True(result.Succeeded);
        Assert.Null(runtime.ExecutedTask);
        Assert.Equal(1, localNavigator.CallCount);
    }

    [Fact]
    public async Task RunAsync_TeleportLocalPlanExecutesTeleportTaskBeforeLocalNavigator()
    {
        var task = CreateTask();
        var plan = new RouteNavigationPlan
        {
            Succeeded = true,
            CompletionMode = RoutePlanCompletionMode.LocalOnly,
            Task = task,
            UsesTeleport = true,
            FrontierNode = new RouteNavigationNode { NodeId = "spawn", MapName = "Teyvat", X = 50, Y = 0 },
            TargetImagePoint = new RouteGraphPoint(100, 0)
        };
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            ExecutionPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            ArrivalPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(100, 0)),
            Execution = TargetNavigationExecutionResult.Completed()
        };
        var localNavigator = new FakeLocalNavigator();

        var result = await new TargetNavigationWorkflow(
                new FakePlanner(plan),
                runtime,
                localNavigator,
                new IdentityCoordinateConverter())
            .RunAsync(CreateRequest(new RouteGraphPoint(100, 0)));

        Assert.True(result.Succeeded);
        Assert.Same(task, runtime.ExecutedTask);
        Assert.Equal(1, localNavigator.CallCount);
        Assert.Equal(50, localNavigator.Request!.RemainingGameDistance, precision: 2);
    }

    [Fact]
    public async Task RunAsync_GraphRevisionChangeInvalidatesExistingPlan()
    {
        var request = CreateRequest(execute: false);
        var current = new RouteGraphPoint(10, 20);
        var planner = new FakePlanner(new RouteNavigationPlan
        {
            Succeeded = true,
            CompletionMode = RoutePlanCompletionMode.Complete,
            Task = CreateTask()
        })
        {
            EffectiveGraphRevision = "revision-2"
        };
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", current)
        };

        var result = await new TargetNavigationWorkflow(planner, runtime)
            .RunAsync(request, CreateReusablePlan(request, current, CreateTask()));

        Assert.True(result.Succeeded);
        Assert.False(result.ReusedExistingPlan);
        Assert.Equal(1, planner.CallCount);
    }

    [Fact]
    public async Task RunAsync_LocalNavigationExecutesTargetActionAfterArrival()
    {
        var plan = new RouteNavigationPlan
        {
            Succeeded = true,
            CompletionMode = RoutePlanCompletionMode.LocalOnly,
            FrontierNode = new RouteNavigationNode { NodeId = "current", MapName = "Teyvat", X = 0, Y = 0 },
            TargetImagePoint = new RouteGraphPoint(50, 0)
        };
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            ExecutionPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            ArrivalPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(50, 0)),
            Execution = TargetNavigationExecutionResult.Completed()
        };

        var result = await new TargetNavigationWorkflow(
                new FakePlanner(plan),
                runtime,
                new FakeLocalNavigator(),
                new IdentityCoordinateConverter())
            .RunAsync(CreateRequest(
                new RouteGraphPoint(50, 0),
                targetAction: ActionEnum.Mining.Code,
                targetActionParams: "ore"));

        Assert.True(result.Succeeded);
        var actionTask = Assert.Single(runtime.ExecutedTasks);
        var actionWaypoint = Assert.Single(actionTask.Positions);
        Assert.Equal(ActionEnum.Mining.Code, actionWaypoint.Action);
        Assert.Equal("ore", actionWaypoint.ActionParams);
        Assert.Equal(WaypointType.Target.Code, actionWaypoint.Type);
    }

    [Fact]
    public async Task RunAsync_LocalTargetActionFailureIsNotReportedAsCompleted()
    {
        var plan = new RouteNavigationPlan
        {
            Succeeded = true,
            CompletionMode = RoutePlanCompletionMode.LocalOnly,
            FrontierNode = new RouteNavigationNode { NodeId = "current", MapName = "Teyvat", X = 0, Y = 0 },
            TargetImagePoint = new RouteGraphPoint(50, 0)
        };
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            ExecutionPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            Execution = TargetNavigationExecutionResult.Failed(
                TargetNavigationFailureCode.ExecutionFailed,
                "target action failed")
        };

        var result = await new TargetNavigationWorkflow(
                new FakePlanner(plan),
                runtime,
                new FakeLocalNavigator(),
                new IdentityCoordinateConverter())
            .RunAsync(CreateRequest(new RouteGraphPoint(50, 0), targetAction: ActionEnum.Mining.Code));

        Assert.False(result.Succeeded);
        Assert.Equal(TargetNavigationState.ExecutionFailed, result.FinalState);
        var actionTask = Assert.Single(runtime.ExecutedTasks);
        Assert.Equal(ActionEnum.Mining.Code, Assert.Single(actionTask.Positions).Action);
        Assert.Contains("target action failed", result.Failure?.Message);
    }

    [Fact]
    public async Task RunAsync_DoesNotReportCompletedWhenFinalPositionIsOutsideArrivalThreshold()
    {
        var runtime = new FakeRuntime
        {
            Preparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            ArrivalPreparation = TargetNavigationPreparationResult.Ready("Teyvat", new RouteGraphPoint(0, 0)),
            Execution = TargetNavigationExecutionResult.Completed()
        };
        var planner = new FakePlanner(new RouteNavigationPlan
        {
            Succeeded = true,
            CompletionMode = RoutePlanCompletionMode.Complete,
            Task = CreateTask()
        });

        var result = await new TargetNavigationWorkflow(
                planner,
                runtime,
                coordinateConverter: new IdentityCoordinateConverter())
            .RunAsync(CreateRequest(new RouteGraphPoint(100, 0)));

        Assert.False(result.Succeeded);
        Assert.Equal(TargetNavigationState.ExecutionFailed, result.FinalState);
        Assert.Contains("距目标仍有", result.Failure?.Message);
    }

    private static TargetNavigationRequest CreateRequest(
        RouteGraphPoint? target = null,
        bool allowTeleport = true,
        RouteGraphPoint? lastKnownCurrent = null,
        bool execute = true,
        string? targetAction = null,
        string? targetActionParams = null)
    {
        return new TargetNavigationRequest
        {
            MapName = "Teyvat",
            MapMatchMethod = "TemplateMatch",
            TargetImagePoint = target ?? new RouteGraphPoint(100, 200),
            TaskName = "地图目标导航",
            TargetAction = targetAction,
            TargetActionParams = targetActionParams,
            LastKnownCurrentImagePoint = lastKnownCurrent,
            Execute = execute,
            Options = new RouteNavigationPlanOptions
            {
                AllowTeleport = allowTeleport,
                AllowUnknownStartConnector = true,
                AllowUnknownTargetConnector = true
            }
        };
    }

    private static RouteNavigationPlan CreateReusablePlan(TargetNavigationRequest request, RouteGraphPoint current, PathingTask task)
    {
        var planRequest = request.BuildPlanRequest(current);
        return new RouteNavigationPlan
        {
            Succeeded = true,
            CompletionMode = RoutePlanCompletionMode.Complete,
            Task = task,
            Request = planRequest,
            Options = request.Options,
            EffectiveGraphRevision = "revision-1",
            PlanningOptionsFingerprint = RouteNavigationPlanningFingerprint.Compute(request.Options)
        };
    }

    private static PathingTask CreateTask()
    {
        return new PathingTask
        {
            Info = new PathingTaskInfo { Name = "地图目标导航", MapName = "Teyvat" },
            Positions =
            [
                new Waypoint { X = 1, Y = 2, Type = WaypointType.Path.Code, MoveMode = MoveModeEnum.Walk.Code },
                new Waypoint { X = 3, Y = 4, Type = WaypointType.Target.Code, MoveMode = MoveModeEnum.Walk.Code }
            ]
        };
    }

    private sealed class FakePlanner(RouteNavigationPlan plan, List<string>? events = null) : IRouteNavigationPlanner
    {
        public string EffectiveGraphRevision { get; init; } = "revision-1";

        public int CallCount { get; private set; }

        public List<RouteNavigationPlanRequest> Requests { get; } = [];

        public bool TryPlan(RouteNavigationPlanRequest request, out RouteNavigationPlan result, RouteNavigationPlanOptions? options = null)
        {
            CallCount++;
            Requests.Add(request);
            events?.Add("plan");
            result = plan;
            return plan.Succeeded;
        }
    }

    private sealed class FakeRuntime(List<string>? events = null) : ITargetNavigationRuntime
    {
        private bool _executionReady;

        public TargetNavigationPreparationResult Preparation { get; init; } =
            TargetNavigationPreparationResult.Failed(TargetNavigationFailureCode.CurrentPositionUnrecognized);

        public TargetNavigationExecutionResult Execution { get; init; } = TargetNavigationExecutionResult.Completed();

        public TargetNavigationPreparationResult? ExecutionPreparation { get; init; }

        public TargetNavigationPreparationResult? ArrivalPreparation { get; init; }

        public PathingTask? ExecutedTask { get; private set; }

        public List<PathingTask> ExecutedTasks { get; } = [];

        public int ReleaseCount { get; private set; }

        public int PlanningPositionCallCount { get; private set; }

        public Task<TargetNavigationPreparationResult> ResolvePlanningPositionAsync(
            string expectedMapName,
            string? mapMatchMethod,
            CancellationToken cancellationToken)
        {
            PlanningPositionCallCount++;
            events?.Add("planning-position");
            return Task.FromResult(_executionReady
                ? ArrivalPreparation ?? TargetNavigationPreparationResult.Ready(
                    expectedMapName,
                    new RouteGraphPoint(100, 200))
                : Preparation);
        }

        public Task<TargetNavigationPreparationResult> WaitUntilReadyAsync(
            string expectedMapName,
            string? mapMatchMethod,
            RouteNavigationCostOptions costOptions,
            CancellationToken cancellationToken)
        {
            events?.Add("wait-ready");
            _executionReady = true;
            return Task.FromResult(ExecutionPreparation ?? Preparation);
        }

        public Task<TargetNavigationExecutionResult> ExecuteAsync(PathingTask task, CancellationToken cancellationToken)
        {
            ExecutedTask = task;
            ExecutedTasks.Add(task);
            events?.Add("execute");
            return Task.FromResult(Execution);
        }

        public void ReleaseAllInputs()
        {
            ReleaseCount++;
            events?.Add("release");
        }
    }

    private sealed class FakeLocalNavigator : ILocalTargetNavigator
    {
        public int CallCount { get; private set; }

        public LocalTargetNavigationRequest? Request { get; private set; }

        public Task<LocalTargetNavigationResult> NavigateAsync(
            LocalTargetNavigationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Request = request;
            return Task.FromResult(LocalTargetNavigationResult.Completed(LocalNavigationCompletionMode.Icon));
        }
    }

    private sealed class IdentityCoordinateConverter : IRouteCoordinateConverter
    {
        public bool TryImageToGame(string mapName, string? mapMatchMethod, RouteGraphPoint imagePoint, out RouteGamePoint gamePoint)
        {
            gamePoint = new RouteGamePoint(imagePoint.X, imagePoint.Y);
            return true;
        }

        public bool TryGameToImage(string mapName, string? mapMatchMethod, RouteGamePoint gamePoint, out RouteGraphPoint imagePoint)
        {
            imagePoint = new RouteGraphPoint(gamePoint.X, gamePoint.Y);
            return true;
        }
    }
}
