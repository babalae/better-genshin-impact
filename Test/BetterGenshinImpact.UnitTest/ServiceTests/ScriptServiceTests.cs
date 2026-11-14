using BetterGenshinImpact.Service;
using Microsoft.Extensions.Time.Testing;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

public class ScriptServiceTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RunRepeatedlyAsync_CancelDuringInterval_ShouldNotExecuteNextIteration()
    {
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var executionCount = 0;

        var running = ScriptService.RunRepeatedlyAsync(2, () =>
        {
            executionCount++;
            return Task.FromResult(true);
        }, cancellation.Token, timeProvider);

        Assert.Equal(1, executionCount);
        Assert.False(running.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => running.WaitAsync(TestTimeout));
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task RunRepeatedlyAsync_IterationReturnsNormallyAfterCancellation_ShouldStop()
    {
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var finishIteration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionCount = 0;

        var running = ScriptService.RunRepeatedlyAsync(2, async () =>
        {
            executionCount++;
            await finishIteration.Task;
            return true;
        }, cancellation.Token, timeProvider);

        Assert.Equal(1, executionCount);
        cancellation.Cancel();
        finishIteration.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => running.WaitAsync(TestTimeout));
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task RunRepeatedlyAsync_AlreadyCancelled_ShouldNotExecute()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var timeProvider = new FakeTimeProvider();
        var executionCount = 0;

        var running = ScriptService.RunRepeatedlyAsync(2, () =>
        {
            executionCount++;
            return Task.FromResult(true);
        }, cancellation.Token, timeProvider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => running.WaitAsync(TestTimeout));
        Assert.Equal(0, executionCount);
    }

    [Fact]
    public async Task RunRepeatedlyAsync_ShouldWaitForIntervalBeforeRepeating()
    {
        var timeProvider = new FakeTimeProvider();
        var executionCount = 0;

        var running = ScriptService.RunRepeatedlyAsync(2, () =>
        {
            executionCount++;
            // 第二轮跳过间隔，避免测试依赖下一次异步定时器的注册时机。
            return Task.FromResult(executionCount == 1);
        }, CancellationToken.None, timeProvider);

        Assert.Equal(1, executionCount);
        timeProvider.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(1, executionCount);
        Assert.False(running.IsCompleted);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await running.WaitAsync(TestTimeout);

        Assert.Equal(2, executionCount);
    }

    [Fact]
    public async Task RunRepeatedlyAsync_SkipIntervalAfterCancellation_ShouldStillStop()
    {
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var executionCount = 0;

        var running = ScriptService.RunRepeatedlyAsync(1, () =>
        {
            executionCount++;
            cancellation.Cancel();
            return Task.FromResult(false);
        }, cancellation.Token, timeProvider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => running.WaitAsync(TestTimeout));
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task RunRepeatedlyAsync_SkipIntervals_ShouldKeepRequestedExecutionCount()
    {
        var timeProvider = new FakeTimeProvider();
        var startTime = timeProvider.GetUtcNow();
        var executionCount = 0;

        await ScriptService.RunRepeatedlyAsync(3, () =>
        {
            executionCount++;
            return Task.FromResult(false);
        }, CancellationToken.None, timeProvider).WaitAsync(TestTimeout);

        Assert.Equal(3, executionCount);
        Assert.Equal(startTime, timeProvider.GetUtcNow());
    }
}
