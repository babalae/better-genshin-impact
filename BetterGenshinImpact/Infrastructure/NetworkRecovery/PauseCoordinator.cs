using System;
using System.Linq;
using System.Threading;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public sealed class PauseCoordinator : IPauseCoordinator
{
    private readonly INetworkPauseGate _networkPauseGate;
    private readonly INetworkHealthMonitor _networkHealthMonitor;
    private readonly IRecoverySession _recoverySession;
    private readonly ILogger<PauseCoordinator> _logger;
    private int _pauseSideEffectsApplied;

    public PauseCoordinator(
        INetworkPauseGate networkPauseGate,
        INetworkHealthMonitor networkHealthMonitor,
        IRecoverySession recoverySession,
        ILogger<PauseCoordinator> logger)
    {
        _networkPauseGate = networkPauseGate;
        _networkHealthMonitor = networkHealthMonitor;
        _recoverySession = recoverySession;
        _logger = logger;
    }

    public bool IsPaused => RunnerContext.Instance.IsSuspend ||
                            (_networkPauseGate.IsNetworkPaused &&
                             !_recoverySession.IsCurrentRecoveryExecution);

    public void ToggleManualPause()
    {
        RunnerContext.Instance.IsSuspend = !RunnerContext.Instance.IsSuspend;
    }

    public void WaitIfPaused(CancellationToken cancellationToken = default)
    {
        var effectiveCancellationToken = cancellationToken.CanBeCanceled
            ? cancellationToken
            : GetTaskCancellationToken();

        if (IsPaused)
        {
            // 长按类操作可能停在本线程的 KeyDown 与 KeyUp 之间；副作用是共享一次性的，这里各自释放
            Simulation.ReleaseAllKey();
        }

        try
        {
            while (IsPaused)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                ApplyPauseSideEffects();

                if (_networkPauseGate.IsNetworkPaused && !_recoverySession.IsCurrentRecoveryExecution)
                {
                    _logger.LogDebug("网络恢复中，任务暂停等待恢复结果");
                    _networkHealthMonitor.RequestCheck(effectiveCancellationToken);
                }

                Thread.Sleep(1000);
            }
        }
        finally
        {
            // 按副作用是否真的应用过释放，不用入口快照：暂停可能在进入等待前一刻才落下。
            // 未应用时 ReleasePauseSideEffects 自身即空操作。
            ReleasePauseSideEffects();
        }
    }

    /// <summary>任务取消令牌。Clear() 会并发释放 CTS，此时退化为不可取消。</summary>
    private static CancellationToken GetTaskCancellationToken()
    {
        try
        {
            return CancellationContext.Instance.Cts.Token;
        }
        catch (ObjectDisposedException)
        {
            return CancellationToken.None;
        }
    }

    private void ApplyPauseSideEffects()
    {
        if (Interlocked.Exchange(ref _pauseSideEffectsApplied, 1) != 0)
        {
            return;
        }

        Simulation.ReleaseAllKey();
        RunnerContext.Instance.StopAutoPick();
        foreach (var suspendable in RunnerContext.Instance.SuspendableDictionary.Values.ToArray())
        {
            suspendable.Suspend();
        }

        _logger.LogWarning(RunnerContext.Instance.IsSuspend ? "快捷键触发暂停，等待解除" : "网络探测失败，任务暂停等待恢复");
    }

    private void ReleasePauseSideEffects()
    {
        if (Interlocked.Exchange(ref _pauseSideEffectsApplied, 0) == 0)
        {
            return;
        }

        RunnerContext.Instance.ResumeAutoPick();
        foreach (var suspendable in RunnerContext.Instance.SuspendableDictionary.Values.ToArray())
        {
            suspendable.Resume();
        }

        _logger.LogWarning("任务暂停已解除，继续当前任务上下文");
    }
}
