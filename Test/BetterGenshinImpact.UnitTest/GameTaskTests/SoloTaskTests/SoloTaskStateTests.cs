using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.ViewModel;
using BetterGenshinImpact.ViewModel.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.SoloTaskTests;

/// <summary>
/// 独立任务状态机、传输模型和按钮展示状态测试。
/// </summary>
public sealed class SoloTaskStateTests
{
    /// <summary>
    /// 验证重复启动、精确停止和停止期间继续占用实例的状态机约束。
    /// </summary>
    [Fact]
    public async Task ExecutionService_ShouldSerializeStartAndStopByRunId()
    {
        var completion = new TaskCompletionSource<SoloTaskRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken executionToken = default;
        Action? markRunning = null;
        var service = new SoloTaskExecutionService(
            (_, cancellationToken, runningCallback) =>
            {
                executionToken = cancellationToken;
                markRunning = runningCallback;
                return completion.Task;
            },
            NullLogger<SoloTaskExecutionService>.Instance);
        var request = new SoloTaskStartRequest
        {
            RunId = Guid.NewGuid(),
            TaskType = SoloTaskType.AutoWood
        };

        var firstStart = await service.StartAsync(request);
        var repeatedStart = await service.StartAsync(request);
        var conflictingStart = await service.StartAsync(new SoloTaskStartRequest
        {
            RunId = Guid.NewGuid(),
            TaskType = SoloTaskType.AutoFight
        });

        Assert.True(firstStart.Accepted);
        Assert.True(repeatedStart.Accepted);
        Assert.Equal(SoloTaskRunState.Starting, service.GetStatus().State);
        Assert.False(conflictingStart.Accepted);

        var startingVersion = service.GetStatus().Version;
        markRunning!();
        Assert.Equal(SoloTaskRunState.Running, service.GetStatus().State);
        Assert.True(service.GetStatus().Version > startingVersion);

        var wrongStop = await service.StopAsync(new SoloTaskStopRequest { RunId = Guid.NewGuid() });
        var acceptedStop = await service.StopAsync(new SoloTaskStopRequest { RunId = request.RunId });
        var repeatedStop = await service.StopAsync(new SoloTaskStopRequest { RunId = request.RunId });
        var startWhileStopping = await service.StartAsync(new SoloTaskStartRequest
        {
            RunId = Guid.NewGuid(),
            TaskType = SoloTaskType.AutoFight
        });

        Assert.False(wrongStop.Accepted);
        Assert.True(acceptedStop.Accepted);
        Assert.True(repeatedStop.Accepted);
        Assert.True(executionToken.IsCancellationRequested);
        Assert.Equal(SoloTaskRunState.Stopping, service.GetStatus().State);
        Assert.False(startWhileStopping.Accepted);

        var stoppingVersion = service.GetStatus().Version;
        completion.SetResult(new SoloTaskRunResult { Outcome = SoloTaskRunOutcome.Cancelled });
        await WaitUntilAsync(() => service.GetStatus().State == SoloTaskRunState.Idle);
        Assert.True(service.GetStatus().Version > stoppingVersion);

        var lateRepeatedStop = await service.StopAsync(new SoloTaskStopRequest { RunId = request.RunId });
        Assert.True(lateRepeatedStop.Accepted);
    }

    /// <summary>
    /// 验证任务请求只携带任务类型和任务专用参数，不再包含整份配置快照。
    /// </summary>
    [Fact]
    public void StartDefinition_ShouldCloneParametersWithoutConfigSnapshot()
    {
        var parameters = new JObject { ["roundNum"] = 3 };
        var definition = new SoloTaskStartDefinition
        {
            TaskType = SoloTaskType.AutoWood,
            Parameters = parameters
        };

        var request = definition.CreateRequest();
        parameters["roundNum"] = 9;
        var json = JsonConvert.SerializeObject(request);

        Assert.Equal(3, request.Parameters!["roundNum"]!.Value<int>());
        Assert.DoesNotContain("ConfigSnapshot", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 验证任务异常后会释放实例占用、保留错误信息，并允许下一次运行重新占用。
    /// </summary>
    [Fact]
    public async Task ExecutionService_ShouldReleaseSlotAfterFailure()
    {
        var service = new SoloTaskExecutionService(
            (_, _, markRunning) =>
            {
                markRunning();
                return Task.FromResult(new SoloTaskRunResult
                {
                    Outcome = SoloTaskRunOutcome.Failed,
                    ErrorMessage = "模拟任务失败"
                });
            },
            NullLogger<SoloTaskExecutionService>.Instance);
        SoloTaskRunCompletedEventArgs? completedEvent = null;
        service.RunCompleted += (_, eventArgs) => completedEvent = eventArgs;

        var first = await service.StartAsync(new SoloTaskStartRequest
        {
            RunId = Guid.NewGuid(),
            TaskType = SoloTaskType.AutoWood
        });
        await WaitUntilAsync(() => service.GetStatus().State == SoloTaskRunState.Idle);

        Assert.True(first.Accepted);
        Assert.Equal("模拟任务失败", service.GetStatus().ErrorMessage);
        Assert.Equal(SoloTaskRunOutcome.Failed, completedEvent?.Result.Outcome);

        var second = await service.StartAsync(new SoloTaskStartRequest
        {
            RunId = Guid.NewGuid(),
            TaskType = SoloTaskType.AutoWood
        });
        Assert.True(second.Accepted);
    }

    /// <summary>
    /// 验证单实例、多实例、处理中和被其他任务占用时的按钮文字及可用性。
    /// </summary>
    [Fact]
    public void ButtonState_ShouldReflectInstanceAndRunState()
    {
        var state = new SoloTaskButtonState(SoloTaskType.AutoWood);

        state.Update(1, 0, SoloTaskRunState.Idle, false, false);
        Assert.Equal("启动", state.ButtonText);
        Assert.True(state.CanInteract);

        state.Update(1, 1, SoloTaskRunState.Starting, false, false);
        Assert.Equal("启动中", state.ButtonText);
        Assert.False(state.CanInteract);

        state.Update(1, 1, SoloTaskRunState.Running, false, false);
        Assert.Equal("停止", state.ButtonText);
        Assert.True(state.CanInteract);

        state.Update(1, 1, SoloTaskRunState.Stopping, false, false);
        Assert.Equal("停止中", state.ButtonText);
        Assert.False(state.CanInteract);

        state.Update(2, 1, SoloTaskRunState.Idle, false, false);
        Assert.Equal("管理（1）", state.ButtonText);
        Assert.True(state.CanInteract);

        state.Update(1, 0, SoloTaskRunState.Idle, true, false);
        Assert.False(state.CanInteract);

        state.Update(2, 0, SoloTaskRunState.Idle, false, true);
        Assert.Equal("处理中…", state.ButtonText);
        Assert.False(state.CanInteract);
    }

    /// <summary>
    /// 验证管理窗口只允许空闲实例启动当前任务、或停止当前任务自己的运行。
    /// </summary>
    [Fact]
    public void InstanceRow_ShouldRejectOtherTaskAndTransitionStates()
    {
        var row = new SoloTaskInstanceRowViewModel(new InstanceEndpoint
        {
            InstanceType = BetterGiInstanceType.Primary,
            ProcessId = 1
        });

        row.Update(SoloTaskStatusSnapshot.Idle(), SoloTaskType.AutoWood, true, false);
        Assert.Equal("可启动", row.StatusText);
        Assert.True(row.CanOperate);

        row.Update(new SoloTaskStatusSnapshot
        {
            RunId = Guid.NewGuid(),
            TaskType = SoloTaskType.AutoFight,
            State = SoloTaskRunState.Running
        }, SoloTaskType.AutoWood, true, false);
        Assert.Contains("自动战斗", row.StatusText);
        Assert.False(row.CanOperate);

        row.Update(new SoloTaskStatusSnapshot
        {
            RunId = Guid.NewGuid(),
            TaskType = SoloTaskType.AutoWood,
            State = SoloTaskRunState.Running
        }, SoloTaskType.AutoWood, true, false);
        Assert.Equal("停止", row.ActionText);
        Assert.True(row.CanOperate);

        row.Update(row.Status, SoloTaskType.AutoWood, false, false);
        Assert.Equal("离线", row.StatusText);
        Assert.False(row.CanOperate);
    }

    /// <summary>
    /// 等待后台状态机完成一次异步清理。
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
