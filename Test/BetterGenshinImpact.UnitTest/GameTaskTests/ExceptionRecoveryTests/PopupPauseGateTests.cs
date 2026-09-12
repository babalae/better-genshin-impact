using System;
using System.Threading;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.ExceptionRecovery;
using BetterGenshinImpact.Infrastructure.NetworkRecovery;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ExceptionRecoveryTests;

public class PopupPauseGateTests
{
    [Fact]
    public void Gate_ShouldCarryReasonUntilCleared()
    {
        var gate = new PopupPauseGate();
        Assert.False(gate.IsPopupPaused);
        Assert.Null(gate.LastReason);

        gate.EnterPopupPause("检测到游戏异常弹窗");
        Assert.True(gate.IsPopupPaused);
        Assert.Equal("检测到游戏异常弹窗", gate.LastReason);

        gate.ClearPopupPause();
        Assert.False(gate.IsPopupPaused);
        Assert.Null(gate.LastReason);
    }

    [Fact]
    public void Gate_RepeatedEnterShouldNotLoseTheOuterHold()
    {
        var gate = new PopupPauseGate();
        gate.EnterPopupPause("第一次");

        // 重入只更新原因，不改变挂起状态；Clear 一次即彻底释放。
        gate.EnterPopupPause("第二次");
        Assert.True(gate.IsPopupPaused);
        Assert.Equal("第二次", gate.LastReason);

        gate.ClearPopupPause();
        Assert.False(gate.IsPopupPaused);
    }

    [Fact]
    public void Coordinator_ShouldPauseOnPopupGateAlone()
    {
        var networkGate = new NetworkPauseGate();
        var popupGate = new PopupPauseGate();
        var coordinator = CreateCoordinator(networkGate, popupGate);

        RunnerContext.Instance.IsSuspend = false;
        try
        {
            Assert.False(coordinator.IsPaused);

            popupGate.EnterPopupPause("检测到游戏异常弹窗");
            Assert.True(coordinator.IsPaused);

            popupGate.ClearPopupPause();
            Assert.False(coordinator.IsPaused);
        }
        finally
        {
            popupGate.ClearPopupPause();
            networkGate.ClearNetworkPause();
            RunnerContext.Instance.IsSuspend = false;
        }
    }

    [Fact]
    public void Coordinator_ShouldHonorCancellationWhilePopupGateHolds()
    {
        var networkGate = new NetworkPauseGate();
        var popupGate = new PopupPauseGate();
        var coordinator = CreateCoordinator(networkGate, popupGate);

        RunnerContext.Instance.IsSuspend = false;
        popupGate.EnterPopupPause("检测到游戏异常弹窗");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            // 取消优先于挂起等待
            Assert.Throws<OperationCanceledException>(() => coordinator.WaitIfPaused(cancellation.Token));
        }
        finally
        {
            popupGate.ClearPopupPause();
            networkGate.ClearNetworkPause();
            RunnerContext.Instance.IsSuspend = false;
        }
    }

    private static PauseCoordinator CreateCoordinator(INetworkPauseGate networkGate, IPopupPauseGate popupGate)
    {
        return new PauseCoordinator(
            networkGate,
            popupGate,
            new StubNetworkHealthMonitor(),
            new RecoverySession(),
            NullLogger<PauseCoordinator>.Instance);
    }

    private sealed class StubNetworkHealthMonitor : INetworkHealthMonitor
    {
        public NetworkHealthSnapshot? LastSnapshot => null;

        public void RequestCheck(CancellationToken cancellationToken = default)
        {
        }
    }
}
