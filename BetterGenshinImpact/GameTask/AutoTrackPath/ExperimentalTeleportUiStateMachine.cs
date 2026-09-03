using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

internal enum ExperimentalTeleportUiState
{
    Unknown1,
    Unknown2,
    MainWorld,
    MapMain,
    AreaList,
    TeleportCandidateList,
    TeleportDetails,
    Loading,
}

internal enum ExperimentalTeleportUiOperation
{
    EnsureMapMain,
    OpenAreaList,
    SelectArea,
    ConfirmTeleport,
}

internal sealed class ExperimentalTeleportUiContext
{
    public required ExperimentalTeleportUiOperation Operation { get; init; }
    public required string MapName { get; init; }
    public string? AreaName { get; init; }
    public GiTpPosition? TargetTp { get; init; }
    public Action? FallbackClick { get; init; }
    public bool FallbackUsed { get; set; }
    public int CandidateClickAttempts { get; set; }
    public int ConfirmClickAttempts { get; set; }
    public int UnknownRecoveryAttempts { get; set; }
    public bool UseAreaOcr { get; set; }
    public bool UnknownDelayStarted { get; set; }
    public long UnknownDetectedAt { get; set; }
    public bool LoadingObserved { get; set; }
    public bool LoadingFallbackLogged { get; set; }
    public long ConfirmIssuedAt { get; set; }
    public long NextBlessingCheckAt { get; set; }
}

/// <summary>
/// 实验传送专用的 UI 状态循环。
/// </summary>
internal sealed class ExperimentalTeleportUiStateMachine
{
    private const int UnknownRecheckDelayMilliseconds = 200;
    private const int ExpectedStateTimeoutMilliseconds = 500;
    private const int ExpectedStateDetectionIntervalMilliseconds = 50;
    private const int StateTransitionSettleDelayMilliseconds = 100;
    private const int AreaAtlasMatchTimeoutMilliseconds = 500;
    private const int AreaAtlasMatchIntervalMilliseconds = 50;
    private const int CandidateClickRetryCount = 2;
    private const int ConfirmClickRetryCount = 2;
    private const int UnknownRecoveryRetryCount = 2;
    private const int MaximumStateIterations = 1000;
    private const int TeleportMinimumCompletionMilliseconds = 1000;
    private const int TeleportLoadingFallbackMilliseconds = 5000;
    private const int BlessingCheckIntervalMilliseconds = 1000;

    private readonly TpTask _host;
    private readonly ExperimentalTeleportRegionAtlas _regionAtlas;
    private readonly TpConfig _config;
    private readonly CancellationToken _cancellationToken;

    public ExperimentalTeleportUiStateMachine(
        TpTask host,
        ExperimentalTeleportRegionAtlas regionAtlas,
        TpConfig config,
        CancellationToken cancellationToken)
    {
        _host = host;
        _regionAtlas = regionAtlas;
        _config = config;
        _cancellationToken = cancellationToken;
    }

    public Task EnsureMapMainAsync(string mapName)
    {
        return RunPhaseAsync(
            new ExperimentalTeleportUiContext
            {
                Operation = ExperimentalTeleportUiOperation.EnsureMapMain,
                MapName = mapName,
            },
            ExperimentalTeleportUiState.MapMain,
            ExperimentalTeleportUiState.MapMain);
    }

    public async Task SwitchAreaAsync(string areaName, string mapName)
    {
        await EnsureMapMainAsync(mapName);

        var openAreaListContext = new ExperimentalTeleportUiContext
        {
            Operation = ExperimentalTeleportUiOperation.OpenAreaList,
            MapName = mapName,
            AreaName = areaName,
        };
        await RunPhaseAsync(
            openAreaListContext,
            ExperimentalTeleportUiState.AreaList,
            ExperimentalTeleportUiState.MapMain);

        await RunPhaseAsync(
            new ExperimentalTeleportUiContext
            {
                Operation = ExperimentalTeleportUiOperation.SelectArea,
                MapName = mapName,
                AreaName = areaName,
                UseAreaOcr = openAreaListContext.UseAreaOcr,
            },
            ExperimentalTeleportUiState.MapMain,
            ExperimentalTeleportUiState.AreaList);
    }

    public Task ConfirmTeleportAsync(string mapName, GiTpPosition targetTp, Action? fallbackClick)
    {
        return RunPhaseAsync(
            new ExperimentalTeleportUiContext
            {
                Operation = ExperimentalTeleportUiOperation.ConfirmTeleport,
                MapName = mapName,
                TargetTp = targetTp,
                FallbackClick = fallbackClick,
            },
            ExperimentalTeleportUiState.MainWorld,
            ExperimentalTeleportUiState.TeleportDetails);
    }

    private async Task RunPhaseAsync(
        ExperimentalTeleportUiContext context,
        ExperimentalTeleportUiState targetState,
        ExperimentalTeleportUiState initialExpectedState)
    {
        ResetUnknownDetection(context);
        LogDetailed(
            "========== 实验传送状态循环启动，目标状态：{TargetState}，入口预期：{ExpectedState} ==========",
            targetState,
            initialExpectedState);

        var currentState = await WaitForExpectedStateAsync(context, initialExpectedState, "阶段入口");
        for (var iteration = 0; iteration < MaximumStateIterations; iteration++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (currentState == targetState)
            {
                LogDetailed(
                    "========== 实验传送状态循环完成，到达目标状态：{State} ==========",
                    currentState);
                return;
            }

            LogDetailed(
                "实验传送状态循环迭代 {Iteration}，当前状态：{State}",
                iteration,
                currentState);
            var previousState = currentState;
            currentState = await HandleStateAsync(context, currentState);
            if (currentState != previousState)
            {
                await Delay(GetOperationDelay(StateTransitionSettleDelayMilliseconds), _cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"实验传送状态循环达到最大迭代次数 {MaximumStateIterations}，未到达目标状态 {targetState}");
    }

    private Task<ExperimentalTeleportUiState> HandleStateAsync(
        ExperimentalTeleportUiContext context,
        ExperimentalTeleportUiState state)
    {
        return state switch
        {
            ExperimentalTeleportUiState.Unknown1 => HandleUnknown1Async(context),
            ExperimentalTeleportUiState.Unknown2 => HandleUnknown2Async(context),
            ExperimentalTeleportUiState.MainWorld => HandleMainWorldAsync(context),
            ExperimentalTeleportUiState.MapMain => HandleMapMainAsync(context),
            ExperimentalTeleportUiState.AreaList => HandleAreaListAsync(context),
            ExperimentalTeleportUiState.TeleportCandidateList => HandleTeleportCandidateListAsync(context),
            ExperimentalTeleportUiState.TeleportDetails => HandleTeleportDetailsAsync(context),
            ExperimentalTeleportUiState.Loading => HandleLoadingAsync(context),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }

    private async Task<ExperimentalTeleportUiState> HandleUnknown1Async(
        ExperimentalTeleportUiContext context)
    {
        var recheckDelay = GetOperationDelay(UnknownRecheckDelayMilliseconds);
        var elapsed = Environment.TickCount64 - context.UnknownDetectedAt;
        var remaining = Math.Max(0, recheckDelay - (int)elapsed);
        if (remaining > 0)
        {
            await Delay(remaining, _cancellationToken);
        }

        return DetectCurrentState(context);
    }

    private async Task<ExperimentalTeleportUiState> HandleUnknown2Async(
        ExperimentalTeleportUiContext context)
    {
        if (context.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            if (context.ConfirmIssuedAt > 0)
            {
                var fallbackDelay = GetOperationDelay(TeleportLoadingFallbackMilliseconds);
                var elapsed = Environment.TickCount64 - context.ConfirmIssuedAt;
                if (elapsed < fallbackDelay)
                {
                    await Delay(
                        Math.Min(
                            GetOperationDelay(ExpectedStateDetectionIntervalMilliseconds),
                            fallbackDelay - (int)elapsed),
                        _cancellationToken);
                    return DetectCurrentState(context);
                }
            }

            throw new TeleportTargetLocalizationException("传送点交互界面连续两次无法识别");
        }

        if (++context.UnknownRecoveryAttempts > UnknownRecoveryRetryCount)
        {
            throw new InvalidOperationException("实验传送界面恢复次数达到上限");
        }

        await new ReturnMainUiTask().Start(_cancellationToken);
        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MainWorld, "未知界面恢复");
    }

    private async Task<ExperimentalTeleportUiState> HandleMainWorldAsync(
        ExperimentalTeleportUiContext context)
    {
        await _host.OpenBigMapUi(1, context.MapName);
        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MapMain, "打开大地图");
    }

    private async Task<ExperimentalTeleportUiState> HandleMapMainAsync(
        ExperimentalTeleportUiContext context)
    {
        if (context.Operation == ExperimentalTeleportUiOperation.OpenAreaList)
        {
            _host.OpenExperimentalAreaList();
            context.UseAreaOcr = !await WaitForAreaAtlasMatchAsync(context.AreaName);
            return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.AreaList, "打开国家列表");
        }

        if (context.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            if (context.FallbackUsed || context.FallbackClick == null)
            {
                throw new TeleportTargetLocalizationException("点击传送点后未出现可识别的交互面板");
            }

            context.FallbackUsed = true;
            context.FallbackClick();
            return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.TeleportDetails, "重新点击传送点");
        }

        return await WaitForExpectedStateAsync(context, GetExpectedState(context.Operation), "地图主界面处理");
    }

    private async Task<ExperimentalTeleportUiState> HandleAreaListAsync(
        ExperimentalTeleportUiContext context)
    {
        if (context.Operation == ExperimentalTeleportUiOperation.SelectArea &&
            context.AreaName is { } areaName)
        {
            if (!context.UseAreaOcr)
            {
                bool clicked;
                using (var capture = CaptureToRectArea())
                {
                    clicked = _regionAtlas.TryClick(capture, areaName);
                }

                if (clicked)
                {
                    return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MapMain, "图集选择国家");
                }

                context.UseAreaOcr = true;
                LogDetailed(
                    "实验传送地区图集点击前匹配丢失，保持国家列表并切换 OCR：{Area}",
                    areaName);
            }

            if (await _host.TrySelectExperimentalArea(areaName))
            {
                return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MapMain, "OCR 选择国家");
            }

            LogDetailed("实验传送地区 OCR 未命中，保持国家列表继续重试：{Area}", areaName);
            await Delay(GetOperationDelay(ExpectedStateDetectionIntervalMilliseconds), _cancellationToken);
            return DetectCurrentState(context);
        }

        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MapMain, "关闭国家列表");
    }

    private async Task<ExperimentalTeleportUiState> HandleTeleportCandidateListAsync(
        ExperimentalTeleportUiContext context)
    {
        if (context.Operation != ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MapMain, "关闭传送点候选列表");
        }

        if (++context.CandidateClickAttempts > CandidateClickRetryCount ||
            !await _host.TryClickExperimentalMapChooseCandidate(context.TargetTp))
        {
            throw new TpPointNotActivate("传送点候选列表中没有可用目标");
        }

        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.TeleportDetails, "选择传送点候选");
    }

    private async Task<ExperimentalTeleportUiState> HandleTeleportDetailsAsync(
        ExperimentalTeleportUiContext context)
    {
        if (context.Operation != ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MapMain, "关闭传送详情");
        }

        if (context.ConfirmIssuedAt > 0)
        {
            var retryDelay = GetOperationDelay(TeleportLoadingFallbackMilliseconds);
            var elapsed = Environment.TickCount64 - context.ConfirmIssuedAt;
            if (elapsed < retryDelay)
            {
                await Delay(
                    Math.Min(
                        GetOperationDelay(ExpectedStateDetectionIntervalMilliseconds),
                        retryDelay - (int)elapsed),
                    _cancellationToken);
                return DetectCurrentState(context);
            }
        }

        if (++context.ConfirmClickAttempts > ConfirmClickRetryCount)
        {
            throw new TeleportTargetLocalizationException("传送确认操作达到重试上限");
        }

        await _host.PressExperimentalTeleportConfirmKey();
        context.LoadingObserved = false;
        context.LoadingFallbackLogged = false;
        context.ConfirmIssuedAt = Environment.TickCount64;
        context.NextBlessingCheckAt =
            context.ConfirmIssuedAt + GetOperationDelay(BlessingCheckIntervalMilliseconds);
        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.Loading, "确认传送");
    }

    private async Task<ExperimentalTeleportUiState> HandleLoadingAsync(
        ExperimentalTeleportUiContext context)
    {
        if (Environment.TickCount64 >= context.NextBlessingCheckAt)
        {
            await _host.HandleExperimentalLoadingInterruption();
            context.NextBlessingCheckAt =
                Environment.TickCount64 + GetOperationDelay(BlessingCheckIntervalMilliseconds);
        }

        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MainWorld, "等待传送完成");
    }

    private async Task<ExperimentalTeleportUiState> WaitForExpectedStateAsync(
        ExperimentalTeleportUiContext context,
        ExperimentalTeleportUiState expectedState,
        string operation)
    {
        var timeout = GetDetectionTimeout(ExpectedStateTimeoutMilliseconds);
        var interval = GetOperationDelay(ExpectedStateDetectionIntervalMilliseconds);
        var stopwatch = Stopwatch.StartNew();
        var detectedState = DetectCurrentState(context);
        if (detectedState == expectedState)
        {
            LogExpectedStateReached(operation, expectedState, stopwatch.ElapsedMilliseconds);
            return detectedState;
        }

        LogDetailed(
            "实验传送状态与预期不符：operation={Operation} expected={ExpectedState} detected={DetectedState}，继续检测最多 {TimeoutMilliseconds}ms",
            operation,
            expectedState,
            detectedState,
            timeout);
        while (stopwatch.ElapsedMilliseconds < timeout)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var remaining = timeout - (int)stopwatch.ElapsedMilliseconds;
            await Delay(Math.Min(interval, remaining), _cancellationToken);
            detectedState = DetectCurrentState(context);
            if (detectedState == expectedState)
            {
                LogExpectedStateReached(operation, expectedState, stopwatch.ElapsedMilliseconds);
                return detectedState;
            }
        }

        LogDetailed(
            "实验传送等待预期状态超时：operation={Operation} expected={ExpectedState} accepted={DetectedState} timeout={TimeoutMilliseconds}ms",
            operation,
            expectedState,
            detectedState,
            timeout);
        return detectedState;
    }

    private ExperimentalTeleportUiState DetectCurrentState(ExperimentalTeleportUiContext context)
    {
        using var capture = CaptureToRectArea();

        if (_host.IsExperimentalTeleportDetails(capture))
        {
            return AcceptKnownState(context, ExperimentalTeleportUiState.TeleportDetails);
        }

        if (_host.HasExperimentalMapChooseCandidate(capture, context.TargetTp))
        {
            return AcceptKnownState(context, ExperimentalTeleportUiState.TeleportCandidateList);
        }

        var isInBigMapUi = Bv.IsInBigMapUi(capture);
        if (isInBigMapUi)
        {
            return AcceptKnownState(
                context,
                _host.HasExperimentalMapMainControls(capture)
                    ? ExperimentalTeleportUiState.MapMain
                    : ExperimentalTeleportUiState.AreaList);
        }

        var isInMainUi = Bv.IsInMainUi(capture);
        if (context.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport &&
            context.ConfirmIssuedAt > 0)
        {
            var elapsed = Environment.TickCount64 - context.ConfirmIssuedAt;
            if (isInMainUi)
            {
                if (context.LoadingObserved &&
                    elapsed >= GetOperationDelay(TeleportMinimumCompletionMilliseconds))
                {
                    return AcceptKnownState(context, ExperimentalTeleportUiState.MainWorld);
                }

                if (!context.LoadingObserved &&
                    elapsed >= GetOperationDelay(TeleportLoadingFallbackMilliseconds))
                {
                    if (!context.LoadingFallbackLogged)
                    {
                        context.LoadingFallbackLogged = true;
                        Logger.LogWarning(
                            "实验传送确认后未识别到 Loading，但已等待 {ElapsedMilliseconds}ms 并处于主界面，按传送成功处理",
                            elapsed);
                    }

                    return AcceptKnownState(context, ExperimentalTeleportUiState.MainWorld);
                }
            }
            else
            {
                context.LoadingObserved = true;
                return AcceptKnownState(context, ExperimentalTeleportUiState.Loading);
            }

            return ResolveUnknownState(context);
        }

        if (context.Operation != ExperimentalTeleportUiOperation.ConfirmTeleport && isInMainUi)
        {
            return AcceptKnownState(context, ExperimentalTeleportUiState.MainWorld);
        }

        return ResolveUnknownState(context);
    }

    private static ExperimentalTeleportUiState AcceptKnownState(
        ExperimentalTeleportUiContext context,
        ExperimentalTeleportUiState state)
    {
        ResetUnknownDetection(context);
        context.UnknownRecoveryAttempts = 0;
        return state;
    }

    private ExperimentalTeleportUiState ResolveUnknownState(ExperimentalTeleportUiContext context)
    {
        if (!context.UnknownDelayStarted)
        {
            context.UnknownDelayStarted = true;
            context.UnknownDetectedAt = Environment.TickCount64;
            return ExperimentalTeleportUiState.Unknown1;
        }

        return Environment.TickCount64 - context.UnknownDetectedAt >=
               GetOperationDelay(UnknownRecheckDelayMilliseconds)
            ? ExperimentalTeleportUiState.Unknown2
            : ExperimentalTeleportUiState.Unknown1;
    }

    private async Task<bool> WaitForAreaAtlasMatchAsync(string? areaName)
    {
        var timeout = GetDetectionTimeout(AreaAtlasMatchTimeoutMilliseconds);
        var interval = GetOperationDelay(AreaAtlasMatchIntervalMilliseconds);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            bool isVisible;
            using (var capture = CaptureToRectArea())
            {
                isVisible = _regionAtlas.IsVisible(capture, areaName);
            }

            if (isVisible)
            {
                LogDetailed(
                    "实验传送地区图集已就绪：area={Area} elapsed={ElapsedMilliseconds}ms",
                    areaName ?? "未指定",
                    stopwatch.ElapsedMilliseconds);
                return true;
            }

            var remaining = timeout - (int)stopwatch.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                LogDetailed(
                    "实验传送地区图集等待超时，保持国家列表并切换 OCR：area={Area} timeout={TimeoutMilliseconds}ms",
                    areaName ?? "未指定",
                    timeout);
                return false;
            }

            await Delay(Math.Min(interval, remaining), _cancellationToken);
        }
    }

    private static ExperimentalTeleportUiState GetExpectedState(
        ExperimentalTeleportUiOperation operation)
    {
        return operation switch
        {
            ExperimentalTeleportUiOperation.EnsureMapMain => ExperimentalTeleportUiState.MapMain,
            ExperimentalTeleportUiOperation.OpenAreaList => ExperimentalTeleportUiState.AreaList,
            ExperimentalTeleportUiOperation.SelectArea => ExperimentalTeleportUiState.MapMain,
            ExperimentalTeleportUiOperation.ConfirmTeleport => ExperimentalTeleportUiState.TeleportDetails,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
    }

    private void LogExpectedStateReached(
        string operation,
        ExperimentalTeleportUiState expectedState,
        long elapsedMilliseconds)
    {
        LogDetailed(
            "实验传送达到预期状态：operation={Operation} state={State} elapsed={ElapsedMilliseconds}ms",
            operation,
            expectedState,
            elapsedMilliseconds);
    }

    private int GetOperationDelay(int baseDelay)
    {
        var configured = Math.Clamp(
            _config.TeleportOperationDelayMilliseconds,
            TpConfig.MinTeleportOperationDelayMilliseconds,
            TpConfig.MaxTeleportOperationDelayMilliseconds);
        return Math.Max(1, (int)Math.Round(
            baseDelay * configured / (double)TpConfig.DefaultTeleportOperationDelayMilliseconds));
    }

    private int GetDetectionTimeout(int baseTimeout)
    {
        return Math.Max(baseTimeout, GetOperationDelay(baseTimeout));
    }

    private void LogDetailed(string message, params object?[] args)
    {
        if (_config.ExperimentalTeleportDetailedLogs)
        {
            Logger.LogDebug(message, args);
        }
    }

    private static void ResetUnknownDetection(ExperimentalTeleportUiContext context)
    {
        context.UnknownDelayStarted = false;
        context.UnknownDetectedAt = 0;
    }
}
