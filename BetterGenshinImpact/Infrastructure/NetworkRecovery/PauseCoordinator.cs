using System;
using System.Linq;
using System.Threading;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public sealed class PauseCoordinator : IPauseCoordinator, IDisposable
{
    private readonly INetworkPauseGate _networkPauseGate;
    private readonly INetworkHealthMonitor _networkHealthMonitor;
    private readonly IRecoverySession _recoverySession;
    private readonly ILogger<PauseCoordinator> _logger;
    private readonly object _pauseSync = new();
    private readonly Timer _sideEffectWatchdog;
    private bool _pauseSideEffectsApplied;
    private int _pauseWaiters;

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

        // 归还共享副作用不能只靠等待方：等待者可能已被取消，暂停解除后再没有人走进 WaitIfPaused。
        _sideEffectWatchdog = new Timer { AutoReset = true, Interval = 1000 };
        _sideEffectWatchdog.Elapsed += (_, _) => ReleaseSideEffectsWhenPauseCleared();
        _sideEffectWatchdog.Start();
    }

    public void Dispose()
    {
        _sideEffectWatchdog.Stop();
        _sideEffectWatchdog.Dispose();
    }

    public bool IsPaused => RunnerContext.Instance.IsSuspend ||
                            (_networkPauseGate.IsNetworkPaused &&
                             !_recoverySession.IsCurrentRecoveryExecution);

    /// <summary>
    /// 暂停来源是否仍生效（不含"恢复流程自身豁免"那一层）。归还共享副作用必须用它判断：
    /// IsPaused 对恢复执行栈恒为假，用它判断会在别的等待者仍被暂停时把副作用解掉。
    /// </summary>
    private bool IsPauseSourceActive => RunnerContext.Instance.IsSuspend || _networkPauseGate.IsNetworkPaused;

    public void ToggleManualPause()
    {
        RunnerContext.Instance.IsSuspend = !RunnerContext.Instance.IsSuspend;
    }

    public void WaitIfPaused(CancellationToken cancellationToken = default)
    {
        var effectiveCancellationToken = CancellationContext.Instance.ResolveToken(cancellationToken);

        if (IsPaused)
        {
            // 长按类操作可能停在本线程的 KeyDown 与 KeyUp 之间；副作用是共享一次性的，这里各自释放
            Simulation.ReleaseAllKey();
        }

        lock (_pauseSync)
        {
            _pauseWaiters++;
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
            // 最后一个退出的等待者归还共享副作用；暂停来源仍生效时不归还（本等待者被取消也算），
            // 由暂停解除后的下一次 WaitIfPaused 调用兜底。未应用时释放自身即空操作。
            lock (_pauseSync)
            {
                if (--_pauseWaiters == 0 && !IsPauseSourceActive)
                {
                    ReleasePauseSideEffects();
                }
            }
        }
    }

    private void ApplyPauseSideEffects()
    {
        lock (_pauseSync)
        {
            if (_pauseSideEffectsApplied)
            {
                return;
            }

            _pauseSideEffectsApplied = true;
            Simulation.ReleaseAllKey();
            RunnerContext.Instance.StopAutoPick();
            foreach (var suspendable in RunnerContext.Instance.SuspendableDictionary.Values.ToArray())
            {
                suspendable.Suspend();
            }

            _logger.LogWarning(RunnerContext.Instance.IsSuspend ? "快捷键触发暂停，等待解除" : "网络探测失败，任务暂停等待恢复");
        }
    }

    private void ReleasePauseSideEffects()
    {
        if (!_pauseSideEffectsApplied)
        {
            return;
        }

        _pauseSideEffectsApplied = false;
        RunnerContext.Instance.ResumeAutoPick();
        foreach (var suspendable in RunnerContext.Instance.SuspendableDictionary.Values.ToArray())
        {
            suspendable.Resume();
        }

        _logger.LogWarning("任务暂停已解除，继续当前任务上下文");
    }

    /// <summary>独立于等待方的归还者：暂停来源已解除且没有等待者留在暂停循环里（避免把新一轮的副作用解掉）。</summary>
    private void ReleaseSideEffectsWhenPauseCleared()
    {
        try
        {
            lock (_pauseSync)
            {
                if (!IsPauseSourceActive && _pauseWaiters == 0)
                {
                    ReleasePauseSideEffects();
                }
            }
        }
        catch (Exception e)
        {
            // 定时器回调抛异常会直接终止进程
            _logger.LogError(e, "归还暂停副作用失败");
        }
    }
}
