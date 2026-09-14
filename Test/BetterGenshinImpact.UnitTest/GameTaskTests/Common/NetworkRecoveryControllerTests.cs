using System.Collections.Concurrent;
using System.Threading.Channels;
using BetterGenshinImpact.GameTask.Common;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.Common;

public class NetworkRecoveryControllerTests
{
    [Fact]
    public async Task EnterPublishesControllerAcrossExecutionContexts_AndScopeClearsIt()
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
    public async Task OverlappingScopesNeverRestoreAnOlderController()
    {
        var firstProbes = Channel.CreateUnbounded<bool>();
        var secondProbes = Channel.CreateUnbounded<bool>();
        await using var first = CreateController(
            () => "first.test",
            firstProbes,
            _ => Task.FromResult(true));
        await using var second = CreateController(
            () => "second.test",
            secondProbes,
            _ => Task.FromResult(true));

        var firstScope = first.Enter();
        var secondScope = second.Enter();
        Assert.Same(second, NetworkRecoveryController.Current);

        firstScope.Dispose();
        Assert.Same(second, NetworkRecoveryController.Current);

        secondScope.Dispose();
        Assert.Null(NetworkRecoveryController.Current);
    }

    [Fact]
    public async Task ParallelPauseParticipantsMustAllAcknowledgeBeforeRecovery()
    {
        var probes = Channel.CreateUnbounded<bool>();
        await using var controller = CreateController(
            () => "probe.test",
            probes,
            _ => Task.FromResult(true));

        var release = NewSignal();
        var acknowledge = Enumerable.Range(0, 3).Select(_ => NewSignal()).ToArray();
        var registered = Enumerable.Range(0, 3).Select(_ => NewSignal()).ToArray();
        var paused = Enumerable.Range(0, 3).Select(_ => NewSignal()).ToArray();
        var workers = Enumerable.Range(0, 3)
            .Select(i => RunPauseParticipantAsync(controller, registered[i], acknowledge[i], paused[i], release.Task))
            .ToArray();

        try
        {
            await Task.WhenAll(registered.Select(signal => signal.Task));
            acknowledge[0].SetResult();
            await paused[0].Task;
            Assert.False(controller.IsTaskPauseAcknowledged);

            acknowledge[1].SetResult();
            await paused[1].Task;
            Assert.False(controller.IsTaskPauseAcknowledged);

            acknowledge[2].SetResult();
            await paused[2].Task;
            Assert.True(controller.IsTaskPauseAcknowledged);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(workers);
        }
    }

    [Fact]
    public async Task DirectAsyncParticipantsKeepIndependentPauseIdentities()
    {
        var probes = Channel.CreateUnbounded<bool>();
        await using var controller = CreateController(
            () => "probe.test",
            probes,
            _ => Task.FromResult(true));

        var release = NewSignal();
        var acknowledge = Enumerable.Range(0, 3).Select(_ => NewSignal()).ToArray();
        var registered = Enumerable.Range(0, 3).Select(_ => NewSignal()).ToArray();
        var paused = Enumerable.Range(0, 3).Select(_ => NewSignal()).ToArray();
        var workers = Enumerable.Range(0, 3)
            .Select(i => RunDirectPauseParticipantAsync(
                controller, registered[i], acknowledge[i], paused[i], release.Task))
            .ToArray();

        try
        {
            await Task.WhenAll(registered.Select(signal => signal.Task));
            acknowledge[0].SetResult();
            await paused[0].Task;
            Assert.False(controller.IsTaskPauseAcknowledged);

            acknowledge[1].SetResult();
            await paused[1].Task;
            Assert.False(controller.IsTaskPauseAcknowledged);

            acknowledge[2].SetResult();
            await paused[2].Task;
            Assert.True(controller.IsTaskPauseAcknowledged);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(workers);
        }
    }

    [Fact]
    public async Task FinishedParallelBranchNoLongerBlocksPauseAcknowledgement()
    {
        var probes = Channel.CreateUnbounded<bool>();
        await using var controller = CreateController(
            () => "probe.test",
            probes,
            _ => Task.FromResult(true));

        var release = NewSignal();
        var acknowledge = NewSignal();
        var runningRegistered = NewSignal();
        var runningPaused = NewSignal();
        var finishedRegistered = NewSignal();
        var finish = NewSignal();
        var running = RunPauseParticipantAsync(
            controller, runningRegistered, acknowledge, runningPaused, release.Task);
        var finished = Task.Run(async () =>
        {
            using var participant = controller.RegisterTaskPauseParticipant();
            finishedRegistered.SetResult();
            await finish.Task;
        });

        try
        {
            await Task.WhenAll(runningRegistered.Task, finishedRegistered.Task);
            acknowledge.SetResult();
            await runningPaused.Task;
            Assert.False(controller.IsTaskPauseAcknowledged);

            finish.SetResult();
            await finished;
            Assert.True(controller.IsTaskPauseAcknowledged);
        }
        finally
        {
            finish.TrySetResult();
            release.TrySetResult();
            await Task.WhenAll(running, finished);
        }
    }

    [Fact]
    public async Task UnregisteredWaiterCannotAcknowledgeRegisteredParticipant()
    {
        var probes = Channel.CreateUnbounded<bool>();
        await using var controller = CreateController(
            () => "probe.test",
            probes,
            _ => Task.FromResult(true));

        var release = NewSignal();
        var acknowledge = NewSignal();
        var registered = NewSignal();
        var paused = NewSignal();
        var participant = RunPauseParticipantAsync(
            controller, registered, acknowledge, paused, release.Task);

        try
        {
            await registered.Task;
            using var unrelatedWaiter = controller.AcknowledgeTaskPaused();
            Assert.True(controller.HasTaskPauseWaiter);
            Assert.False(controller.IsTaskPauseAcknowledged);

            acknowledge.SetResult();
            await paused.Task;
            Assert.True(controller.IsTaskPauseAcknowledged);
        }
        finally
        {
            release.TrySetResult();
            await participant;
        }
    }

    [Fact]
    public async Task ProbeFallsBackToTcpWhenIcmpIsBlocked()
    {
        var attemptedPorts = new ConcurrentBag<int>();

        var healthy = await NetworkRecoveryController.ProbeNetworkAsync(
            "https://probe.test/path",
            CancellationToken.None,
            icmpProbe: (_, _) => Task.FromResult(false),
            tcpProbe: (host, port, _) =>
            {
                Assert.Equal("probe.test", host);
                attemptedPorts.Add(port);
                return Task.FromResult(port == 443);
            },
            isNetworkAvailable: () => true);

        Assert.True(healthy);
        Assert.Contains(443, attemptedPorts);
        Assert.Contains(80, attemptedPorts);
    }

    [Fact]
    public async Task ProbeSkipsTcpFallbackWhenNoNetworkInterfaceIsAvailable()
    {
        var tcpAttempts = 0;

        var healthy = await NetworkRecoveryController.ProbeNetworkAsync(
            "probe.test",
            CancellationToken.None,
            icmpProbe: (_, _) => Task.FromResult(false),
            tcpProbe: (_, _, _) =>
            {
                Interlocked.Increment(ref tcpAttempts);
                return Task.FromResult(true);
            },
            isNetworkAvailable: () => false);

        Assert.False(healthy);
        Assert.Equal(0, tcpAttempts);
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

    [Fact]
    public async Task ProbeLogsOnlyStateChanges_AndReportsRecoveryResult()
    {
        var probes = Channel.CreateUnbounded<bool>();
        var info = new ConcurrentQueue<string>();
        var warnings = new ConcurrentQueue<string>();
        await using var controller = new NetworkRecoveryController(
            () => "probe.test",
            _ => Task.FromResult(true),
            CancellationToken.None,
            onInfo: info.Enqueue,
            onWarning: warnings.Enqueue,
            probe: (_, token) => probes.Reader.ReadAsync(token).AsTask(),
            interval: TimeSpan.FromMilliseconds(10));

        await probes.Writer.WriteAsync(false);
        await WaitUntilAsync(() => warnings.Count == 1);
        await probes.Writer.WriteAsync(false);
        await WaitUntilAsync(() => warnings.Count == 2);
        await probes.Writer.WriteAsync(false);
        await WaitUntilAsync(() => controller.IsPaused && warnings.Count == 3);
        await probes.Writer.WriteAsync(false);
        await Task.Delay(30);

        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, message => message.Contains("已进入暂停等待恢复状态"));

        await probes.Writer.WriteAsync(true);
        await WaitUntilAsync(() => info.Any(message => message.Contains("网络已恢复")));
        await InvokeUntilAsync(controller, () => !controller.IsPaused);

        Assert.Contains(info, message => message.Contains("恢复流程已启动"));
        Assert.Contains(info, message => message.Contains("已解除网络暂停"));
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

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task RunPauseParticipantAsync(
        NetworkRecoveryController controller,
        TaskCompletionSource registered,
        TaskCompletionSource acknowledge,
        TaskCompletionSource paused,
        Task release) => Task.Run(async () =>
    {
        using var participant = controller.RegisterTaskPauseParticipant();
        registered.SetResult();
        await acknowledge.Task;
        using var pauseAcknowledgement = controller.AcknowledgeTaskPaused();
        paused.SetResult();
        await release;
    });

    private static async Task RunDirectPauseParticipantAsync(
        NetworkRecoveryController controller,
        TaskCompletionSource registered,
        TaskCompletionSource acknowledge,
        TaskCompletionSource paused,
        Task release)
    {
        using var participant = controller.RegisterTaskPauseParticipant();
        registered.SetResult();
        await acknowledge.Task;
        using var pauseAcknowledgement = controller.AcknowledgeTaskPaused();
        paused.SetResult();
        await release;
    }

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
