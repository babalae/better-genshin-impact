using System.Linq;
using System.Threading;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.ExceptionRecovery;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public sealed class PauseCoordinator : IPauseCoordinator
{
    private readonly INetworkPauseGate _networkPauseGate;
    private readonly IPopupPauseGate _popupPauseGate;
    private readonly INetworkHealthMonitor _networkHealthMonitor;
    private readonly IRecoverySession _recoverySession;
    private readonly ILogger<PauseCoordinator> _logger;
    private readonly object _pauseSync = new();
    private bool _pauseSideEffectsApplied;
    private int _pauseWaiters;

    public PauseCoordinator(
        INetworkPauseGate networkPauseGate,
        IPopupPauseGate popupPauseGate,
        INetworkHealthMonitor networkHealthMonitor,
        IRecoverySession recoverySession,
        ILogger<PauseCoordinator> logger)
    {
        _networkPauseGate = networkPauseGate;
        _popupPauseGate = popupPauseGate;
        _networkHealthMonitor = networkHealthMonitor;
        _recoverySession = recoverySession;
        _logger = logger;
    }

    // 网络门与弹窗门并列；恢复流程自身的执行栈豁免挂起
    public bool IsPaused => RunnerContext.Instance.IsSuspend ||
                            ((_networkPauseGate.IsNetworkPaused || _popupPauseGate.IsPopupPaused) &&
                             !_recoverySession.IsCurrentRecoveryExecution);

    /// <summary>
    /// 暂停来源是否仍生效（不含"恢复流程自身豁免"那一层）。归还共享副作用必须用它判断：
    /// IsPaused 对恢复执行栈恒为假，用它判断会在别的等待者仍被暂停时把副作用解掉。
    /// </summary>
    private bool IsPauseSourceActive => RunnerContext.Instance.IsSuspend ||
                                        _networkPauseGate.IsNetworkPaused ||
                                        _popupPauseGate.IsPopupPaused;

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

            _logger.LogWarning(DescribePauseReason());
        }
    }

    /// <summary>挂起原因文案。三种来源区分开，热键优先。</summary>
    private string DescribePauseReason()
    {
        if (RunnerContext.Instance.IsSuspend)
        {
            return "快捷键触发暂停，等待解除";
        }

        return _networkPauseGate.IsNetworkPaused
            ? "网络探测失败，任务暂停等待恢复"
            : $"游戏异常弹窗处理中，任务暂停等待恢复：{_popupPauseGate.LastReason}";
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
}
