using System.Threading.Channels;
using BetterGenshinImpact.GameTask.Common;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.Common;

public class NetworkRecoveryControllerTests
{
    [Fact]
    public async Task EnterPublishesControllerAcrossExecutionContexts_AndScopeRestoresIt()
    {
        var probes = Channel.CreateUnbounded<bool>();
        await using var controller = CreateController(
            () => "probe.test",
            probes,
            _ => Task.FromResult(true));

        Assert.Null(NetworkRecoveryController.Current);
        using (controller.Enter())
        {
            Assert.Same(controller, NetworkRecoveryController.Current);
            Assert.Same(controller, await Task.Run(() => NetworkRecoveryController.Current));

            Assert.False(controller.HasTaskPauseWaiter);
            using (controller.AcknowledgeTaskPaused())
                Assert.True(controller.HasTaskPauseWaiter);
            Assert.False(controller.HasTaskPauseWaiter);
        }
        Assert.Null(NetworkRecoveryController.Current);
    }

    [Fact]
    public async Task ThreeFailuresPause_AndSuccessfulRecoveryResumes()
    {
        var probes = Channel.CreateUnbounded<bool>();
        var recoverySucceeds = false;
        var recoveryCount = 0;
        await using var controller = CreateController(
            () => "probe.test",
            probes,
            _ =>
            {
                Interlocked.Increment(ref recoveryCount);
                return Task.FromResult(recoverySucceeds);
            });

        await probes.Writer.WriteAsync(false);
        await probes.Writer.WriteAsync(false);
        await Task.Delay(30);
        Assert.False(controller.IsPaused);

        await probes.Writer.WriteAsync(false);
        await WaitUntilAsync(() => controller.IsPaused);

        await probes.Writer.WriteAsync(true);
        await InvokeUntilAsync(controller, () => Volatile.Read(ref recoveryCount) == 1);
        Assert.True(controller.IsPaused);

        recoverySucceeds = true;
        await InvokeUntilAsync(controller, () => !controller.IsPaused);
        Assert.Equal(2, recoveryCount);
    }

    [Fact]
    public async Task DisablingMonitoringClearsPendingPause()
    {
        var probes = Channel.CreateUnbounded<bool>();
        string? target = "probe.test";
        await using var controller = CreateController(
            () => target,
            probes,
            _ => Task.FromResult(true));

        await probes.Writer.WriteAsync(false);
        await probes.Writer.WriteAsync(false);
        await probes.Writer.WriteAsync(false);
        await WaitUntilAsync(() => controller.IsPaused);

        target = null;
        await WaitUntilAsync(() => !controller.IsPaused);
    }

    [Fact]
    public async Task TaskCancellationCancelsRunningRecovery()
    {
        var probes = Channel.CreateUnbounded<bool>();
        using var taskCancellation = new CancellationTokenSource();
        var recoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recoveryCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = CreateController(
            () => "probe.test",
            probes,
            async ct =>
            {
                recoveryStarted.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException)
                {
                    recoveryCanceled.SetResult();
                    throw;
                }
                return true;
            }, taskCancellation.Token);

        await probes.Writer.WriteAsync(false);
        await probes.Writer.WriteAsync(false);
        await probes.Writer.WriteAsync(false);
        await WaitUntilAsync(() => controller.IsPaused);
        await probes.Writer.WriteAsync(true);

        var recovery = InvokeUntilStartedAsync(controller, recoveryStarted.Task);
        await recoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        taskCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recovery);
        await recoveryCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static NetworkRecoveryController CreateController(
        Func<string?> getTarget,
        Channel<bool> probes,
        Func<CancellationToken, Task<bool>> recover,
        CancellationToken ct = default) => new(
        getTarget,
        recover,
        ct,
        probe: (_, token) => probes.Reader.ReadAsync(token).AsTask(),
        interval: TimeSpan.FromMilliseconds(10));

    private static async Task InvokeUntilStartedAsync(
        NetworkRecoveryController controller,
        Task started)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!started.IsCompleted)
        {
            await controller.TryRecoverAsync(timeout.Token);
            if (!started.IsCompleted) await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task InvokeUntilAsync(
        NetworkRecoveryController controller,
        Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await controller.TryRecoverAsync(timeout.Token);
            if (!condition()) await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
