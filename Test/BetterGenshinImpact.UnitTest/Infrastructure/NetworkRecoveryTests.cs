using System;
using System.Threading;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging.Abstractions;
using BetterGenshinImpact.Infrastructure.NetworkRecovery;

namespace BetterGenshinImpact.UnitTest.Infrastructure;

public class NetworkRecoveryTests
{
    [Fact]
    public void Snapshot_ShouldCountOnlyConsecutiveFailures()
    {
        var failed = NetworkHealthDecisions.CreateSnapshot(
            new NetworkProbeResult(false, NetworkHealthStatus.DnsFailure, TimeSpan.Zero),
            "example.invalid",
            2,
            DateTimeOffset.UtcNow);
        var recovered = NetworkHealthDecisions.CreateSnapshot(
            new NetworkProbeResult(true, NetworkHealthStatus.Healthy, TimeSpan.FromMilliseconds(20)),
            "example.invalid",
            failed.ConsecutiveFailures,
            DateTimeOffset.UtcNow);

        Assert.Equal(3, failed.ConsecutiveFailures);
        Assert.Equal(0, recovered.ConsecutiveFailures);
        Assert.Equal(NetworkHealthStatus.DnsFailure, failed.Status);
    }

    [Theory]
    [InlineData(2, 3, false)]
    [InlineData(3, 3, true)]
    [InlineData(5, 3, true)]
    public void ShouldPause_ShouldUseConfiguredThreshold(int failures, int threshold, bool expected)
    {
        var snapshot = new NetworkHealthSnapshot(
            DateTimeOffset.UtcNow,
            "example.invalid",
            NetworkHealthStatus.Unreachable,
            failures,
            TimeSpan.Zero);

        Assert.Equal(expected, NetworkHealthDecisions.ShouldPause(snapshot, threshold));
    }

    [Fact]
    public async Task RecoverySession_ShouldPreventDuplicateRecoveryAndPreserveTask()
    {
        var session = new RecoverySession();
        var task = new TaskExecutionContext("daily", "task-1", "领取邮件", 2);
        session.BeginTask(task);

        using var first = session.BeginRecovery();

        Assert.True(session.IsRecovering);
        Assert.True(session.IsCurrentRecoveryExecution);
        Assert.Null(session.BeginRecovery());
        Assert.Equal(task, session.CurrentTask);

        Task<bool> recoveryExecutionFromAnotherContext;
        using (ExecutionContext.SuppressFlow())
        {
            recoveryExecutionFromAnotherContext = Task.Run(() => session.IsCurrentRecoveryExecution);
        }
        Assert.False(await recoveryExecutionFromAnotherContext);

        first!.Dispose();
        Assert.False(session.IsRecovering);
        Assert.False(session.IsCurrentRecoveryExecution);
        session.CompleteTask("task-1");
        Assert.Null(session.CurrentTask);
    }

    [Fact]
    public async Task LoginRecovery_ShouldClearPauseWhenAlreadyAtMainUi()
    {
        var gate = new NetworkPauseGate();
        gate.EnterNetworkPause(new NetworkHealthSnapshot(
            DateTimeOffset.UtcNow,
            "example.invalid",
            NetworkHealthStatus.Unreachable,
            3,
            TimeSpan.Zero));
        var adapter = new StubLoginAdapter(LoginScreenState.MainUi);
        var stateMachine = new LoginRecoveryStateMachine(
            new[] { adapter },
            new RecoverySession(),
            gate,
            NullLogger<LoginRecoveryStateMachine>.Instance);

        var result = await stateMachine.RecoverAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(LoginRecoveryState.Succeeded, result.State);
        Assert.False(gate.IsNetworkPaused);
        Assert.Equal(0, adapter.ConfirmCount);
        Assert.Equal(0, adapter.ReloginCount);
    }

    [Fact]
    public async Task LoginRecovery_ShouldConfirmErrorAndReloginWhenLoginIsRequired()
    {
        var gate = new NetworkPauseGate();
        gate.EnterNetworkPause(new NetworkHealthSnapshot(
            DateTimeOffset.UtcNow,
            "example.invalid",
            NetworkHealthStatus.Unreachable,
            3,
            TimeSpan.Zero));
        var adapter = new StubLoginAdapter(
            LoginScreenState.NetworkError,
            LoginScreenState.LoginRequired)
        {
            ReturnToMainUiResult = true,
            ReloginResult = true
        };
        var stateMachine = new LoginRecoveryStateMachine(
            new[] { adapter },
            new RecoverySession(),
            gate,
            NullLogger<LoginRecoveryStateMachine>.Instance);

        var result = await stateMachine.RecoverAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(LoginRecoveryState.Succeeded, result.State);
        Assert.Equal(1, adapter.ConfirmCount);
        Assert.Equal(1, adapter.ReturnToMainUiCount);
        Assert.Equal(1, adapter.ReloginCount);
        Assert.False(gate.IsNetworkPaused);
    }

    [Fact]
    public void PauseCoordinator_ShouldHonorCancellationBeforeWaiting()
    {
        var gate = new NetworkPauseGate();
        gate.EnterNetworkPause(new NetworkHealthSnapshot(
            DateTimeOffset.UtcNow,
            "example.invalid",
            NetworkHealthStatus.Unreachable,
            3,
            TimeSpan.Zero));
        var coordinator = new PauseCoordinator(
            gate,
            new StubNetworkHealthMonitor(),
            new RecoverySession(),
            NullLogger<PauseCoordinator>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        RunnerContext.Instance.IsSuspend = false;
        Assert.Throws<OperationCanceledException>(() => coordinator.WaitIfPaused(cancellation.Token));
        gate.ClearNetworkPause();
        RunnerContext.Instance.IsSuspend = false;
    }

    private sealed class StubLoginAdapter(params LoginScreenState[] screens) : ILoginAdapter
    {
        private readonly Queue<LoginScreenState> _screens = new(screens);

        public string Name => "测试登录适配器";
        public bool ReturnToMainUiResult { get; set; }
        public bool ReloginResult { get; set; }
        public int ConfirmCount { get; private set; }
        public int ReturnToMainUiCount { get; private set; }
        public int ReloginCount { get; private set; }

        public bool CanHandle() => true;

        public Task<LoginScreenState> DetectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_screens.Count > 0 ? _screens.Dequeue() : LoginScreenState.Unknown);
        }

        public Task<bool> ConfirmNetworkErrorAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfirmCount++;
            return Task.FromResult(true);
        }

        public Task<bool> ReturnToMainUiAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReturnToMainUiCount++;
            return Task.FromResult(ReturnToMainUiResult);
        }

        public Task<bool> ReloginAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReloginCount++;
            return Task.FromResult(ReloginResult);
        }
    }

    private sealed class StubNetworkHealthMonitor : INetworkHealthMonitor
    {
        public NetworkHealthSnapshot? LastSnapshot => null;

        public void RequestCheck(CancellationToken cancellationToken = default)
        {
        }
    }
}
