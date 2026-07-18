using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.TargetNavigation;

public sealed class BetterGiTargetNavigationRuntime(
    IRouteNavigationGraphProvider graphProvider,
    IRouteCurrentPositionResolver? positionResolver = null) : ITargetNavigationRuntime
{
    private readonly IRouteCurrentPositionResolver _positionResolver =
        positionResolver ?? RouteCurrentPositionResolver.Instance;
    private readonly ILogger<BetterGiTargetNavigationRuntime> _logger =
        App.GetLogger<BetterGiTargetNavigationRuntime>();
    private RouteCurrentPosition? _lastKnownPosition;

    public async Task<TargetNavigationPreparationResult> ResolvePlanningPositionAsync(
        string expectedMapName,
        string? mapMatchMethod,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_lastKnownPosition != null)
        {
            return TargetNavigationPreparationResult.Ready(
                _lastKnownPosition.MapName,
                _lastKnownPosition.ImagePoint);
        }

        try
        {
            if (!TaskContext.Instance().IsInitialized)
            {
                await ScriptService.StartGameTask(false);
            }

            using var screen = TaskControl.CaptureToRectArea(true);
            if (_positionResolver.TryResolve(
                    screen,
                    expectedMapName,
                    mapMatchMethod,
                    out var currentPosition))
            {
                _lastKnownPosition = currentPosition;
                return TargetNavigationPreparationResult.Ready(
                    currentPosition.MapName,
                    currentPosition.ImagePoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "目标导航规划阶段无法刷新坐标，将使用最后有效坐标");
        }

        return TargetNavigationPreparationResult.Failed(
            TargetNavigationFailureCode.CurrentPositionUnrecognized,
            "当前界面无法定位，且没有最后一次有效坐标");
    }

    public async Task<TargetNavigationPreparationResult> WaitUntilReadyAsync(
        string expectedMapName,
        string? mapMatchMethod,
        RouteNavigationCostOptions costOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TaskControl.TaskSemaphore.CurrentCount == 0)
        {
            return TargetNavigationPreparationResult.Failed(TargetNavigationFailureCode.TaskRunnerBusy);
        }

        if (!graphProvider.TryGetSnapshot(out _, out var graphStatus))
        {
            return TargetNavigationPreparationResult.Failed(MapGraphFailure(graphStatus));
        }

        try
        {
            await ScriptService.StartGameTask(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "目标导航启动截图器失败");
            return TargetNavigationPreparationResult.Failed(
                TargetNavigationFailureCode.CaptureNotInitialized,
                ex.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var context = TaskContext.Instance();
        var gameHandle = SystemControl.FindGenshinImpactHandle();
        if (gameHandle == IntPtr.Zero)
        {
            return TargetNavigationPreparationResult.Failed(TargetNavigationFailureCode.GameWindowNotFound);
        }

        if (!context.IsInitialized)
        {
            return TargetNavigationPreparationResult.Failed(TargetNavigationFailureCode.CaptureNotInitialized);
        }

        try
        {
            _ = TaskTriggerDispatcher.GlobalGameCapture;
        }
        catch (Exception ex)
        {
            return TargetNavigationPreparationResult.Failed(
                TargetNavigationFailureCode.CaptureNotInitialized,
                ex.Message);
        }

        if (!await ActivateAndVerifyWindowAsync(cancellationToken))
        {
            return TargetNavigationPreparationResult.Failed(TargetNavigationFailureCode.WindowActivationFailed);
        }

        try
        {
            var uiState = CaptureUiState();
            if (uiState.InTalk)
            {
                var talkReady = await WaitForTalkToFinishAsync(
                    costOptions.TalkWaitTimeoutSeconds,
                    cancellationToken);
                if (!talkReady)
                {
                    return TargetNavigationPreparationResult.Failed(
                        TargetNavigationFailureCode.NotInMainUi,
                        $"剧情等待超过 {costOptions.TalkWaitTimeoutSeconds:F0} 秒");
                }

                uiState = CaptureUiState();
            }

            if (!uiState.InMainUi)
            {
                await new ReturnMainUiTask().Start(cancellationToken);
            }

            using var readyScreen = TaskControl.CaptureToRectArea(true);
            if (!Bv.IsInMainUi(readyScreen))
            {
                return TargetNavigationPreparationResult.Failed(TargetNavigationFailureCode.NotInMainUi);
            }

            if (!_positionResolver.TryResolve(
                    readyScreen,
                    expectedMapName,
                    mapMatchMethod,
                    out var currentPosition))
            {
                return TargetNavigationPreparationResult.Failed(
                    TargetNavigationFailureCode.CurrentPositionUnrecognized);
            }

            _lastKnownPosition = currentPosition;

            return TargetNavigationPreparationResult.Ready(
                currentPosition.MapName,
                currentPosition.ImagePoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "目标导航读取实时坐标失败");
            return TargetNavigationPreparationResult.Failed(
                TargetNavigationFailureCode.CurrentPositionUnrecognized,
                ex.Message);
        }
    }

    private static (bool InMainUi, bool InTalk) CaptureUiState()
    {
        using var screen = TaskControl.CaptureToRectArea(true);
        return (Bv.IsInMainUi(screen), Bv.IsInTalkUi(screen));
    }

    private static async Task<bool> WaitForTalkToFinishAsync(
        double timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, timeoutSeconds));
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = CaptureUiState();
            if (state.InMainUi || !state.InTalk)
            {
                return true;
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    public async Task<TargetNavigationExecutionResult> ExecuteAsync(
        PathingTask task,
        CancellationToken cancellationToken)
    {
        if (TaskControl.TaskSemaphore.CurrentCount == 0)
        {
            return TargetNavigationExecutionResult.Failed(TargetNavigationFailureCode.TaskRunnerBusy);
        }

        if (!await ActivateAndVerifyWindowAsync(cancellationToken))
        {
            return TargetNavigationExecutionResult.Failed(TargetNavigationFailureCode.WindowActivationFailed);
        }

        var actionStarted = false;
        var activationFailedInsideRunner = false;
        var successEnd = false;
        var manualCancellation = false;
        var lostFocus = false;
        Exception? executionException = null;

        await new TaskRunner().RunThreadAsync(async () =>
        {
            actionStarted = true;
            if (!await ActivateAndVerifyWindowAsync(cancellationToken))
            {
                activationFailedInsideRunner = true;
                return;
            }

            using var localCancellation = cancellationToken.Register(
                () => CancellationContext.Instance.ManualCancel());
            using var focusMonitorCts = new CancellationTokenSource();
            var focusMonitor = MonitorGameFocusAsync(
                focusMonitorCts.Token,
                () => lostFocus = true);
            var pathExecutor = new PathExecutor(CancellationContext.Instance.Cts.Token)
            {
                PartyConfig = new PathingPartyConfig { AutoFightEnabled = false }
            };

            try
            {
                await pathExecutor.Pathing(task);
                successEnd = pathExecutor.SuccessEnd;
            }
            catch (Exception ex)
            {
                executionException = ex;
                throw;
            }
            finally
            {
                manualCancellation = cancellationToken.IsCancellationRequested ||
                                     CancellationContext.Instance.IsManualStop;
                focusMonitorCts.Cancel();
                try
                {
                    await focusMonitor;
                }
                catch (OperationCanceledException)
                {
                    // 正常停止前台监控。
                }

                Simulation.ReleaseAllKey();
            }
        });

        if (!actionStarted)
        {
            return TargetNavigationExecutionResult.Failed(TargetNavigationFailureCode.TaskRunnerBusy);
        }

        if (activationFailedInsideRunner)
        {
            return TargetNavigationExecutionResult.Failed(TargetNavigationFailureCode.WindowActivationFailed);
        }

        if (lostFocus)
        {
            return TargetNavigationExecutionResult.Failed(TargetNavigationFailureCode.GameWindowLostFocus);
        }

        if (manualCancellation || cancellationToken.IsCancellationRequested)
        {
            return TargetNavigationExecutionResult.CancelledByUser();
        }

        if (executionException != null)
        {
            return TargetNavigationExecutionResult.Failed(
                TargetNavigationFailureCode.ExecutionFailed,
                executionException.Message);
        }

        return successEnd
            ? TargetNavigationExecutionResult.Completed()
            : TargetNavigationExecutionResult.Failed(
                TargetNavigationFailureCode.ExecutionFailed,
                "PathExecutor 未到达路线终点");
    }

    public void ReleaseAllInputs()
    {
        Simulation.ReleaseAllKey();
    }

    private static async Task<bool> ActivateAndVerifyWindowAsync(CancellationToken cancellationToken)
    {
        try
        {
            SystemControl.ActivateWindow();
            for (var attempt = 0; attempt < 10; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (SystemControl.IsGenshinImpactActiveByProcess())
                {
                    return true;
                }

                await Task.Delay(50, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static async Task MonitorGameFocusAsync(
        CancellationToken cancellationToken,
        Action onFocusLost)
    {
        while (true)
        {
            await Task.Delay(200, cancellationToken);
            if (SystemControl.IsGenshinImpactActiveByProcess())
            {
                continue;
            }

            onFocusLost();
            Simulation.ReleaseAllKey();
            CancellationContext.Instance.Cancel();
            return;
        }
    }

    private static TargetNavigationFailureCode MapGraphFailure(RouteNavigationGraphLoadStatus status)
    {
        return status switch
        {
            RouteNavigationGraphLoadStatus.FileMissing => TargetNavigationFailureCode.GraphFileMissing,
            RouteNavigationGraphLoadStatus.Empty => TargetNavigationFailureCode.GraphEmpty,
            RouteNavigationGraphLoadStatus.Invalid => TargetNavigationFailureCode.GraphInvalid,
            _ => TargetNavigationFailureCode.GraphNotLoaded
        };
    }
}
