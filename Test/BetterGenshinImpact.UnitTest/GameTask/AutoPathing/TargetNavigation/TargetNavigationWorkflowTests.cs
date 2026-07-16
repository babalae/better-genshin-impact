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
            "prepare",
            "status:Planning",
            "plan",
            "plan-ready",
            "status:PlanSucceeded",
            "status:WaitingToStart",
            "status:Executing",
            "execute",
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
    [InlineData(TargetNavigationFailureCode.CurrentPositionUnrecognized, TargetNavigationState.PlanFailed)]
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

    private static TargetNavigationRequest CreateRequest(
        RouteGraphPoint? target = null,
        bool allowTeleport = true)
    {
        return new TargetNavigationRequest
        {
            MapName = "Teyvat",
            MapMatchMethod = "TemplateMatch",
            TargetImagePoint = target ?? new RouteGraphPoint(100, 200),
            TaskName = "地图目标导航",
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
            Task = task,
            Request = planRequest,
            Options = request.Options
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
        public int CallCount { get; private set; }

        public bool TryPlan(RouteNavigationPlanRequest request, out RouteNavigationPlan result, RouteNavigationPlanOptions? options = null)
        {
            CallCount++;
            events?.Add("plan");
            result = plan;
            return plan.Succeeded;
        }
    }

    private sealed class FakeRuntime(List<string>? events = null) : ITargetNavigationRuntime
    {
        public TargetNavigationPreparationResult Preparation { get; init; } =
            TargetNavigationPreparationResult.Failed(TargetNavigationFailureCode.CurrentPositionUnrecognized);

        public TargetNavigationExecutionResult Execution { get; init; } = TargetNavigationExecutionResult.Completed();

        public PathingTask? ExecutedTask { get; private set; }

        public int ReleaseCount { get; private set; }

        public Task<TargetNavigationPreparationResult> PrepareAsync(string expectedMapName, string? mapMatchMethod, CancellationToken cancellationToken)
        {
            events?.Add("prepare");
            return Task.FromResult(Preparation);
        }

        public Task<TargetNavigationExecutionResult> ExecuteAsync(PathingTask task, CancellationToken cancellationToken)
        {
            ExecutedTask = task;
            events?.Add("execute");
            return Task.FromResult(Execution);
        }

        public void ReleaseAllInputs()
        {
            ReleaseCount++;
            events?.Add("release");
        }
    }
}
