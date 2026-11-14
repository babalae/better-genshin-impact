using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 实验传送适配层。目标解析、地区切换、缩放判定、选点和重试均复用 <see cref="TpTask"/>。
/// </summary>
internal sealed class ExperimentalTeleportTask : IDisposable
{
    private const int TeleportTimeoutMilliseconds = 60_000;
    private const int MaximumTeleportAttempts = 3;
    private const int StateTransitionBudgetPerAttempt = 10;
    private const int TimeoutSafetyMarginMilliseconds = 5_000;

    private readonly TpConfig _config;
    private readonly TpTask _host;
    private readonly ExperimentalTeleportDrag _drag;
    private readonly ExperimentalTeleportUiStateMachine _uiStateMachine;

    private ExperimentalTeleportTask(CancellationToken cancellationToken)
    {
        _config = TaskContext.Instance().Config.TpConfig;
        _host = new TpTask(cancellationToken);
        _drag = new ExperimentalTeleportDrag(_config, cancellationToken);
        _uiStateMachine = new ExperimentalTeleportUiStateMachine(_host, _config, cancellationToken);
    }

    public static async Task<(double, double)> Run(
        CancellationToken cancellationToken,
        double tpX,
        double tpY,
        string mapName,
        bool force)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutMilliseconds = GetTeleportTimeoutMilliseconds();
        timeoutCts.CancelAfter(timeoutMilliseconds);
        using var task = new ExperimentalTeleportTask(timeoutCts.Token);
        try
        {
            return await task.RunAsync(tpX, tpY, mapName, force);
        }
        catch (OperationCanceledException ex) when (
            !cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"实验传送超过 {timeoutMilliseconds / 1000} 秒", ex);
        }
    }

    private static int GetTeleportTimeoutMilliseconds()
    {
        var config = TaskContext.Instance().Config.TpConfig;
        var mapOpenTimeout = config.GetEffectiveExperimentalTeleportMapOpenTimeoutMilliseconds();
        var stateTransitionTimeout = config.GetEffectiveExperimentalTeleportStateTransitionTimeoutMilliseconds();
        var initialDelay = config.GetEffectiveExperimentalTeleportStateRecognitionInitialDelayMilliseconds();
        var operationDelay = config.TeleportOperationDelayMilliseconds;

        long perAttempt = mapOpenTimeout;
        perAttempt = SaturatingAdd(perAttempt, SaturatingMultiply(stateTransitionTimeout, StateTransitionBudgetPerAttempt));
        perAttempt = SaturatingAdd(perAttempt, SaturatingMultiply(initialDelay, StateTransitionBudgetPerAttempt));
        perAttempt = SaturatingAdd(perAttempt, SaturatingMultiply(operationDelay, StateTransitionBudgetPerAttempt));
        perAttempt = SaturatingAdd(perAttempt, TimeoutSafetyMarginMilliseconds);

        var total = SaturatingMultiply(perAttempt, MaximumTeleportAttempts);
        total = Math.Max(TeleportTimeoutMilliseconds, total);
        return total >= int.MaxValue ? int.MaxValue : (int)Math.Max(1L, total);
    }

    private static long SaturatingAdd(long left, long right)
    {
        return left >= long.MaxValue - right ? long.MaxValue : left + right;
    }

    private static long SaturatingMultiply(long value, int multiplier)
    {
        return value <= 0 || multiplier <= 0 || value > long.MaxValue / multiplier
            ? value <= 0 ? 0 : long.MaxValue
            : value * multiplier;
    }

    private async Task<(double, double)> RunAsync(
        double tpX,
        double tpY,
        string mapName,
        bool force)
    {
        LogConfigSnapshot();
        return await _host.RunExperimentalTeleport(
            tpX,
            tpY,
            mapName,
            force,
            _drag,
            _uiStateMachine);
    }

    private void LogConfigSnapshot()
    {
        if (!_config.IsExperimentalTeleportDetailedLoggingEnabled)
        {
            return;
        }

        Logger.LogDebug(
            "实验传送配置：distanceCorrection={DistanceCorrection:0.00} maxStep={MaxStep} stepInterval={StepInterval}ms " +
            "stateInterval={StateInterval}ms stateInitialDelay={StateInitialDelay}ms stateTimeout={StateTimeout}ms teleportOperationDelay={TeleportOperationDelay}ms " +
            "mapOpenTimeout={MapOpenTimeout}ms mapOpenRepressInterval={MapOpenRepressInterval}ms " +
            "dragStartDelay={DragStartDelay}ms dragReleaseDelay={DragReleaseDelay}ms",
            _config.GetEffectiveExperimentalTeleportDragDistanceCorrection(),
            _config.GetEffectiveExperimentalTeleportMaxSingleStepDistancePixels(),
            _config.GetEffectiveExperimentalTeleportDragStepIntervalMilliseconds(),
            _config.GetEffectiveExperimentalTeleportStateRecognitionIntervalMilliseconds(),
            _config.GetEffectiveExperimentalTeleportStateRecognitionInitialDelayMilliseconds(),
            _config.GetEffectiveExperimentalTeleportStateTransitionTimeoutMilliseconds(),
            _config.TeleportOperationDelayMilliseconds,
            _config.GetEffectiveExperimentalTeleportMapOpenTimeoutMilliseconds(),
            _config.GetEffectiveExperimentalTeleportMapOpenRepressIntervalMilliseconds(),
            _config.GetEffectiveExperimentalTeleportDragStartDelayMilliseconds(),
            _config.GetEffectiveExperimentalTeleportDragReleaseDelayMilliseconds());
    }

    public void Dispose()
    {
    }
}
