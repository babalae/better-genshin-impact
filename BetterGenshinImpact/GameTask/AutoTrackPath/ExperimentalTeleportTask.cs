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
        timeoutCts.CancelAfter(TeleportTimeoutMilliseconds);
        using var task = new ExperimentalTeleportTask(timeoutCts.Token);
        try
        {
            return await task.RunAsync(tpX, tpY, mapName, force);
        }
        catch (OperationCanceledException ex) when (
            !cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"实验传送超过 {TeleportTimeoutMilliseconds / 1000} 秒", ex);
        }
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
        if (!_config.ExperimentalTeleportDetailedLogs)
        {
            return;
        }

        Logger.LogDebug(
            "实验传送配置：distanceCorrection={DistanceCorrection:0.00} maxStep={MaxStep} stepInterval={StepInterval}ms " +
            "stateInterval={StateInterval}ms stateTimeout={StateTimeout}ms teleportOperationDelay={TeleportOperationDelay}ms " +
            "mapOpenTimeout={MapOpenTimeout}ms mapOpenRepressInterval={MapOpenRepressInterval}ms " +
            "dragStartDelay={DragStartDelay}ms dragReleaseDelay={DragReleaseDelay}ms",
            _config.ExperimentalTeleportDragDistanceCorrection,
            _config.ExperimentalTeleportMaxSingleStepDistancePixels,
            _config.ExperimentalTeleportDragStepIntervalMilliseconds,
            _config.ExperimentalTeleportStateRecognitionIntervalMilliseconds,
            _config.ExperimentalTeleportStateTransitionTimeoutMilliseconds,
            _config.TeleportOperationDelayMilliseconds,
            _config.ExperimentalTeleportMapOpenTimeoutMilliseconds,
            _config.ExperimentalTeleportMapOpenRepressIntervalMilliseconds,
            _config.ExperimentalTeleportDragStartDelayMilliseconds,
            _config.ExperimentalTeleportDragReleaseDelayMilliseconds);
    }

    public void Dispose()
    {
    }
}
