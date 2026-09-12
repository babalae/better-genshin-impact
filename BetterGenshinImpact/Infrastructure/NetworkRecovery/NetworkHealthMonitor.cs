using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public sealed class NetworkHealthMonitor : INetworkHealthMonitor
{
    private const int ProbeIntervalSeconds = 5;
    private const int FailureThreshold = 3;
    private const int ProbeTimeoutMilliseconds = 1500;

    private readonly INetworkHealthProbe _probe;
    private readonly INetworkPauseGate _pauseGate;
    private readonly ILoginRecoveryStateMachine _recoveryStateMachine;
    private readonly ILogger<NetworkHealthMonitor> _logger;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly object _stateSync = new();
    private DateTimeOffset _lastCheckAt = DateTimeOffset.MinValue;
    private int _consecutiveFailures;
    private NetworkHealthSnapshot? _lastSnapshot;

    public NetworkHealthMonitor(
        INetworkHealthProbe probe,
        INetworkPauseGate pauseGate,
        ILoginRecoveryStateMachine recoveryStateMachine,
        ILogger<NetworkHealthMonitor> logger)
    {
        _probe = probe;
        _pauseGate = pauseGate;
        _recoveryStateMachine = recoveryStateMachine;
        _logger = logger;
    }

    public NetworkHealthSnapshot? LastSnapshot
    {
        get
        {
            lock (_stateSync)
            {
                return _lastSnapshot;
            }
        }
    }

    public void RequestCheck(CancellationToken cancellationToken = default)
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            return;
        }

        var config = TaskContext.Instance().Config.OtherConfig;
        if (!IsMonitoringEffective(config))
        {
            ResetMonitoringState();
            return;
        }

        if (!_checkGate.Wait(0))
        {
            return;
        }

        _ = Task.Run(() => CheckAsync(cancellationToken));
    }

    /// <summary>开关关闭或探测地址为空时不生效。空地址会被探测实现判为 DnsFailure，不能当成断网。</summary>
    private static bool IsMonitoringEffective(OtherConfig config)
    {
        return config.NetworkHealthMonitoringEnabled && !string.IsNullOrWhiteSpace(config.NetworkProbeTarget);
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var effectiveCancellationToken = cancellationToken;
            var config = TaskContext.Instance().Config.OtherConfig;
            var interval = TimeSpan.FromSeconds(ProbeIntervalSeconds);
            var now = DateTimeOffset.UtcNow;
            lock (_stateSync)
            {
                if (now - _lastCheckAt < interval)
                {
                    return;
                }

                _lastCheckAt = now;
            }

            var probe = await _probe.ProbeAsync(
                config.NetworkProbeTarget,
                ProbeTimeoutMilliseconds,
                effectiveCancellationToken);

            if (!IsMonitoringEffective(config))
            {
                ResetMonitoringState();
                return;
            }

            NetworkHealthSnapshot snapshot;
            lock (_stateSync)
            {
                snapshot = NetworkHealthDecisions.CreateSnapshot(
                    probe,
                    config.NetworkProbeTarget,
                    _consecutiveFailures,
                    now);
                _consecutiveFailures = snapshot.ConsecutiveFailures;
                _lastSnapshot = snapshot;
            }

            if (snapshot.IsHealthy)
            {
                lock (_stateSync)
                {
                    _consecutiveFailures = 0;
                }

                if (_pauseGate.IsNetworkPaused)
                {
                    var result = await _recoveryStateMachine.RecoverAsync(effectiveCancellationToken);
                    if (!result.Succeeded)
                    {
                        _logger.LogWarning("网络已连通但恢复流程未完成：{Message}", result.Message);
                    }
                }

                return;
            }

            if (NetworkHealthDecisions.ShouldPause(snapshot, FailureThreshold))
            {
                _pauseGate.EnterNetworkPause(snapshot);
                _logger.LogWarning("网络探测失败，任务已暂停：{Status}，连续失败 {Count} 次", snapshot.Status,
                    snapshot.ConsecutiveFailures);
            }
        }
        catch (OperationCanceledException)
        {
            // 调用方取消任务时，忽略本次后台探测。
        }
        catch (Exception e)
        {
            _logger.LogError(e, "网络健康监控执行失败");
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private void ResetMonitoringState()
    {
        // 恢复进行中时不清暂停门：清了会让原任务的 WaitIfPaused 提前返回，
        // 与仍在操作界面的登录恢复抢控制权。等恢复自身结束再清。
        if (!IsRecoveryInFlight())
        {
            _pauseGate.ClearNetworkPause();
        }

        lock (_stateSync)
        {
            _consecutiveFailures = 0;
            _lastCheckAt = DateTimeOffset.MinValue;
            _lastSnapshot = null;
        }
    }

    private bool IsRecoveryInFlight()
    {
        return _recoveryStateMachine.State is
            LoginRecoveryState.Detecting or
            LoginRecoveryState.ConfirmingNetworkError or
            LoginRecoveryState.ReturningToMainUi or
            LoginRecoveryState.Relogging;
    }
}
