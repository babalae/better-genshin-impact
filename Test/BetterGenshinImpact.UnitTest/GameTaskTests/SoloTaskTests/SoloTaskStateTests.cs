using BetterGenshinImpact.GameTask.SoloTask.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.SoloTaskTests;

/// <summary>
/// 独立任务执行状态机测试。
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
            TaskId = "auto_wood"
        };

        var firstStart = await service.StartAsync(request);
        var repeatedStart = await service.StartAsync(request);
        var conflictingStart = await service.StartAsync(new SoloTaskStartRequest
        {
            RunId = Guid.NewGuid(),
            TaskId = "auto_fight"
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
            TaskId = "auto_fight"
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
                    Outcome = SoloTaskRunOutcome.Faulted,
                    ErrorMessage = "模拟任务失败"
                });
            },
            NullLogger<SoloTaskExecutionService>.Instance);
        SoloTaskRunCompletedEventArgs? completedEvent = null;
        service.RunCompleted += (_, eventArgs) => completedEvent = eventArgs;

        var first = await service.StartAsync(new SoloTaskStartRequest
        {
            RunId = Guid.NewGuid(),
            TaskId = "auto_wood"
        });
        await WaitUntilAsync(() => service.GetStatus().State == SoloTaskRunState.Idle);

        Assert.True(first.Accepted);
        Assert.Equal("模拟任务失败", service.GetStatus().ErrorMessage);
        Assert.Equal(SoloTaskRunOutcome.Faulted, completedEvent?.Result.Outcome);

        var second = await service.StartAsync(new SoloTaskStartRequest
        {
            RunId = Guid.NewGuid(),
            TaskId = "auto_wood"
        });
        Assert.True(second.Accepted);
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
