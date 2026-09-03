using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.StateMachine;
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
    public bool UseAreaOcr { get; set; }
    public bool UnknownDelayStarted { get; set; }
    public long UnknownDetectedAt { get; set; }
    public bool UnknownRecheckReady { get; set; }
    public bool LoadingObserved { get; set; }
    public long ConfirmIssuedAt { get; set; }
    public long NextBlessingCheckAt { get; set; }
}

/// <summary>
/// 实验传送的 UI 识别、恢复和传送完成状态机。
/// </summary>
internal sealed class ExperimentalTeleportUiStateMachine
    : StateMachineBase<ExperimentalTeleportUiState, ExperimentalTeleportUiContext>
{
    private const int UnknownRecheckDelayMilliseconds = 200;
    private const int AreaAtlasMatchTimeoutMilliseconds = 500;
    private const int AreaAtlasMatchIntervalMilliseconds = 50;
    private const int CandidateClickRetryCount = 2;
    private const int TeleportMinimumCompletionMilliseconds = 1000;
    private const int TeleportLoadingFallbackMilliseconds = 5000;
    private const int BlessingCheckIntervalMilliseconds = 1000;

    private readonly TpTask _host;
    private readonly ExperimentalTeleportRegionAtlas _regionAtlas;
    private readonly TpConfig _config;
    private readonly CancellationToken _cancellationToken;
    private ExperimentalTeleportUiContext? _activeContext;

    protected override ILogger Logger => TaskControl.Logger;
    protected override int DefaultDetectionInterval => 100;
    protected override int StateMachineLoopInterval => 100;
    protected override int DefaultIntermediateTransitionTimeout => 60_000;

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

        RegisterStateMethodsByAttribute();
        RegisterStateTransitions(
            (ExperimentalTeleportUiState.Unknown1,
            [
                ExperimentalTeleportUiState.TeleportDetails,
                ExperimentalTeleportUiState.TeleportCandidateList,
                ExperimentalTeleportUiState.AreaList,
                ExperimentalTeleportUiState.MapMain,
                ExperimentalTeleportUiState.MainWorld,
                ExperimentalTeleportUiState.Loading,
                ExperimentalTeleportUiState.Unknown2,
            ]),
            (ExperimentalTeleportUiState.Unknown2,
            [
                ExperimentalTeleportUiState.MainWorld,
            ]),
            (ExperimentalTeleportUiState.MainWorld,
            [
                ExperimentalTeleportUiState.AreaList,
                ExperimentalTeleportUiState.MapMain,
                ExperimentalTeleportUiState.Unknown1,
            ]),
            (ExperimentalTeleportUiState.MapMain,
            [
                ExperimentalTeleportUiState.AreaList,
                ExperimentalTeleportUiState.TeleportCandidateList,
                ExperimentalTeleportUiState.TeleportDetails,
                ExperimentalTeleportUiState.Unknown1,
            ]),
            (ExperimentalTeleportUiState.AreaList,
            [
                ExperimentalTeleportUiState.MapMain,
                ExperimentalTeleportUiState.Unknown1,
            ]),
            (ExperimentalTeleportUiState.TeleportCandidateList,
            [
                ExperimentalTeleportUiState.TeleportDetails,
                ExperimentalTeleportUiState.MapMain,
                ExperimentalTeleportUiState.Unknown1,
            ]),
            (ExperimentalTeleportUiState.TeleportDetails,
            [
                ExperimentalTeleportUiState.Loading,
                ExperimentalTeleportUiState.MapMain,
                ExperimentalTeleportUiState.Unknown1,
            ]),
            (ExperimentalTeleportUiState.Loading,
            [
                ExperimentalTeleportUiState.MainWorld,
            ]));
    }

    public Task EnsureMapMainAsync(string mapName)
    {
        return RunPhaseAsync(
            new ExperimentalTeleportUiContext
            {
                Operation = ExperimentalTeleportUiOperation.EnsureMapMain,
                MapName = mapName,
            },
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
            ExperimentalTeleportUiState.AreaList);
        await RunPhaseAsync(
            new ExperimentalTeleportUiContext
            {
                Operation = ExperimentalTeleportUiOperation.SelectArea,
                MapName = mapName,
                AreaName = areaName,
                UseAreaOcr = openAreaListContext.UseAreaOcr,
            },
            ExperimentalTeleportUiState.MapMain);
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
            ExperimentalTeleportUiState.MainWorld);
    }

    private async Task RunPhaseAsync(
        ExperimentalTeleportUiContext context,
        ExperimentalTeleportUiState targetState)
    {
        _activeContext = context;
        Initialize(_cancellationToken, ExperimentalTeleportUiState.Unknown1);
        await RunStateMachineUntil(context, targetState);
    }

    [StateDetector(ExperimentalTeleportUiState.TeleportDetails, Order = 10)]
    private bool DetectTeleportDetails(ImageRegion imageRegion)
    {
        return CanDetectKnownState() && _host.IsExperimentalTeleportDetails(imageRegion);
    }

    [StateDetector(ExperimentalTeleportUiState.TeleportCandidateList, Order = 20)]
    private bool DetectTeleportCandidateList(ImageRegion imageRegion)
    {
        return CanDetectKnownState() &&
               _host.HasExperimentalMapChooseCandidate(imageRegion, _activeContext?.TargetTp);
    }

    [StateDetector(ExperimentalTeleportUiState.AreaList, Order = 30)]
    private bool DetectAreaList(ImageRegion imageRegion)
    {
        return CanDetectKnownState() &&
               Bv.IsInBigMapUi(imageRegion) &&
               !_host.HasExperimentalMapMainControls(imageRegion);
    }

    [StateDetector(ExperimentalTeleportUiState.MapMain, Order = 40)]
    private bool DetectMapMain(ImageRegion imageRegion)
    {
        return CanDetectKnownState() && _host.IsExperimentalMapMain(imageRegion);
    }

    [StateDetector(ExperimentalTeleportUiState.MainWorld, Order = 50)]
    private bool DetectMainWorld(ImageRegion imageRegion)
    {
        if (!CanDetectKnownState())
        {
            return false;
        }

        var context = _activeContext;
        if (context?.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            if (context.ConfirmIssuedAt <= 0)
            {
                return false;
            }

            var elapsed = Environment.TickCount64 - context.ConfirmIssuedAt;
            if (context.LoadingObserved)
            {
                if (elapsed < TeleportMinimumCompletionMilliseconds)
                {
                    return false;
                }
            }
            else if (elapsed < GetOperationDelay(TeleportLoadingFallbackMilliseconds))
            {
                return false;
            }
        }

        var isMainWorld = Bv.IsInMainUi(imageRegion);
        if (isMainWorld &&
            context?.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport &&
            !context.LoadingObserved)
        {
            Logger.LogWarning(
                "实验传送确认后未识别到 Loading，但已等待 {ElapsedMilliseconds}ms 并回到主界面，按传送成功处理",
                Environment.TickCount64 - context.ConfirmIssuedAt);
        }

        return isMainWorld;
    }

    [StateDetector(ExperimentalTeleportUiState.Loading, Order = 60)]
    private bool DetectLoading(ImageRegion imageRegion)
    {
        var context = _activeContext;
        if (!CanDetectKnownState() ||
            context?.Operation != ExperimentalTeleportUiOperation.ConfirmTeleport ||
            context.ConfirmIssuedAt <= 0 ||
            Bv.IsInBigMapUi(imageRegion) ||
            Bv.IsInMainUi(imageRegion))
        {
            return false;
        }

        context.LoadingObserved = true;
        return true;
    }

    [StateDetector(ExperimentalTeleportUiState.Unknown2, Order = 90)]
    private bool DetectUnknown2(ImageRegion _)
    {
        return _activeContext?.UnknownRecheckReady == true;
    }

    [StateDetector(ExperimentalTeleportUiState.Unknown1, Order = 100)]
    private bool DetectUnknown1(ImageRegion imageRegion)
    {
        if (CurrentState != ExperimentalTeleportUiState.Unknown1 &&
            IsCurrentStateStillVisible(imageRegion))
        {
            return false;
        }

        if (_activeContext is { } context)
        {
            context.UnknownDelayStarted = true;
            context.UnknownDetectedAt = Environment.TickCount64;
            context.UnknownRecheckReady = false;
        }

        return true;
    }

    [StateHandler(ExperimentalTeleportUiState.Unknown1, RetryTimes = 2)]
    private async Task<StateHandlerResult> HandleUnknown1(ExperimentalTeleportUiContext context)
    {
        if (!context.UnknownDelayStarted)
        {
            context.UnknownDelayStarted = true;
            context.UnknownDetectedAt = Environment.TickCount64;
        }

        var recheckDelay = GetOperationDelay(UnknownRecheckDelayMilliseconds);
        var elapsed = Environment.TickCount64 - context.UnknownDetectedAt;
        var remainingDelay = Math.Max(0, recheckDelay - (int)elapsed);
        if (remainingDelay > 0)
        {
            await Delay(remainingDelay, _cancellationToken);
        }

        context.UnknownRecheckReady = true;
        return StateHandlerResult.Success;
    }

    [StateHandler(ExperimentalTeleportUiState.Unknown2, RetryTimes = 2)]
    private async Task<StateHandlerResult> HandleUnknown2(ExperimentalTeleportUiContext context)
    {
        if (context.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            throw new TeleportTargetLocalizationException("传送点交互界面连续两次无法识别");
        }

        ResetUnknownDetection(context);
        await new ReturnMainUiTask().Start(_cancellationToken);
        using var capture = CaptureToRectArea();
        return Bv.IsInMainUi(capture)
            ? StateHandlerResult.SuccessTo(ExperimentalTeleportUiState.MainWorld)
            : StateHandlerResult.Retry;
    }

    [StateHandler(ExperimentalTeleportUiState.MainWorld, RetryTimes = 2, TransitionTimeout = 5000)]
    private async Task<StateHandlerResult> HandleMainWorld(ExperimentalTeleportUiContext context)
    {
        ResetUnknownDetection(context);
        await _host.OpenBigMapUi(1, context.MapName);
        return StateHandlerResult.SuccessTo(
            ExperimentalTeleportUiState.AreaList,
            ExperimentalTeleportUiState.MapMain,
            ExperimentalTeleportUiState.Unknown1);
    }

    [StateHandler(ExperimentalTeleportUiState.MapMain, RetryTimes = 2, TransitionTimeout = 1500)]
    private async Task<StateHandlerResult> HandleMapMain(ExperimentalTeleportUiContext context)
    {
        ResetUnknownDetection(context);
        if (context.Operation == ExperimentalTeleportUiOperation.OpenAreaList)
        {
            _host.OpenExperimentalAreaList();
            context.UseAreaOcr = !await WaitForAreaAtlasMatchAsync(context.AreaName);
            return StateHandlerResult.SuccessTo(
                ExperimentalTeleportUiState.AreaList,
                ExperimentalTeleportUiState.Unknown1);
        }

        if (context.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            if (context.FallbackUsed || context.FallbackClick == null)
            {
                throw new TeleportTargetLocalizationException("点击传送点后未出现可识别的交互面板");
            }

            context.FallbackUsed = true;
            context.FallbackClick();
            return StateHandlerResult.SuccessTo(
                ExperimentalTeleportUiState.TeleportDetails,
                ExperimentalTeleportUiState.TeleportCandidateList,
                ExperimentalTeleportUiState.Unknown1);
        }

        return StateHandlerResult.Wait;
    }

    [StateHandler(ExperimentalTeleportUiState.AreaList, RetryTimes = 2, TransitionTimeout = 1500)]
    private async Task<StateHandlerResult> HandleAreaList(ExperimentalTeleportUiContext context)
    {
        ResetUnknownDetection(context);
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
                    await Delay(GetOperationDelay(160), _cancellationToken);
                    return StateHandlerResult.SuccessTo(
                        ExperimentalTeleportUiState.MapMain,
                        ExperimentalTeleportUiState.Unknown1);
                }

                context.UseAreaOcr = true;
                Logger.LogDebug("实验传送地区图集点击前匹配丢失，保持国家列表并切换 OCR：{Area}", areaName);
            }

            _cancellationToken.ThrowIfCancellationRequested();
            if (await _host.TrySelectExperimentalArea(areaName))
            {
                return StateHandlerResult.SuccessTo(
                    ExperimentalTeleportUiState.MapMain,
                    ExperimentalTeleportUiState.Unknown1);
            }

            Logger.LogDebug("实验传送地区 OCR 未命中，保持国家列表继续重试：{Area}", areaName);
            return StateHandlerResult.Wait;
        }

        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        await Delay(GetOperationDelay(80), _cancellationToken);
        return StateHandlerResult.SuccessTo(
            ExperimentalTeleportUiState.MapMain,
            ExperimentalTeleportUiState.Unknown1);
    }

    [StateHandler(ExperimentalTeleportUiState.TeleportCandidateList, RetryTimes = CandidateClickRetryCount + 1, TransitionTimeout = 1500)]
    private async Task<StateHandlerResult> HandleTeleportCandidateList(ExperimentalTeleportUiContext context)
    {
        ResetUnknownDetection(context);
        if (context.Operation != ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await Delay(GetOperationDelay(80), _cancellationToken);
            return StateHandlerResult.SuccessTo(
                ExperimentalTeleportUiState.MapMain,
                ExperimentalTeleportUiState.Unknown1);
        }

        if (++context.CandidateClickAttempts > CandidateClickRetryCount ||
            !await _host.TryClickExperimentalMapChooseCandidate(context.TargetTp))
        {
            throw new TpPointNotActivate("传送点候选列表中没有可用目标");
        }

        return StateHandlerResult.SuccessTo(
            ExperimentalTeleportUiState.TeleportDetails,
            ExperimentalTeleportUiState.Unknown1);
    }

    [StateHandler(ExperimentalTeleportUiState.TeleportDetails, RetryTimes = 2, TransitionTimeout = 5000)]
    private async Task<StateHandlerResult> HandleTeleportDetails(ExperimentalTeleportUiContext context)
    {
        ResetUnknownDetection(context);
        if (context.Operation != ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await Delay(GetOperationDelay(80), _cancellationToken);
            return StateHandlerResult.SuccessTo(
                ExperimentalTeleportUiState.MapMain,
                ExperimentalTeleportUiState.Unknown1);
        }

        await _host.PressExperimentalTeleportConfirmKey();
        context.ConfirmIssuedAt = Environment.TickCount64;
        context.NextBlessingCheckAt = context.ConfirmIssuedAt + BlessingCheckIntervalMilliseconds;
        return StateHandlerResult.SuccessTo(
            ExperimentalTeleportUiState.Loading,
            ExperimentalTeleportUiState.MainWorld);
    }

    [StateHandler(ExperimentalTeleportUiState.Loading)]
    private async Task<StateHandlerResult> HandleLoading(ExperimentalTeleportUiContext context)
    {
        if (Environment.TickCount64 >= context.NextBlessingCheckAt)
        {
            await _host.HandleExperimentalLoadingInterruption();
            context.NextBlessingCheckAt = Environment.TickCount64 + BlessingCheckIntervalMilliseconds;
        }

        return StateHandlerResult.Wait;
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

    private async Task<bool> WaitForAreaAtlasMatchAsync(string? areaName)
    {
        var timeout = GetOperationDelay(AreaAtlasMatchTimeoutMilliseconds);
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
                Logger.LogDebug(
                    "实验传送地区图集已就绪：area={Area} elapsed={ElapsedMilliseconds}ms",
                    areaName ?? "未指定",
                    stopwatch.ElapsedMilliseconds);
                return true;
            }

            var remaining = timeout - (int)stopwatch.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                Logger.LogDebug(
                    "实验传送地区图集等待超时，保持国家列表并切换 OCR：area={Area} timeout={TimeoutMilliseconds}ms",
                    areaName ?? "未指定",
                    timeout);
                return false;
            }

            await Delay(Math.Min(interval, remaining), _cancellationToken);
        }
    }

    private bool CanDetectKnownState()
    {
        var context = _activeContext;
        return context == null ||
               !context.UnknownDelayStarted ||
               CurrentState != ExperimentalTeleportUiState.Unknown1 ||
               context.UnknownRecheckReady ||
               Environment.TickCount64 - context.UnknownDetectedAt >=
               GetOperationDelay(UnknownRecheckDelayMilliseconds);
    }

    private bool IsCurrentStateStillVisible(ImageRegion imageRegion)
    {
        return CurrentState switch
        {
            ExperimentalTeleportUiState.MainWorld => DetectMainWorld(imageRegion),
            ExperimentalTeleportUiState.MapMain => DetectMapMain(imageRegion),
            ExperimentalTeleportUiState.AreaList => DetectAreaList(imageRegion),
            ExperimentalTeleportUiState.TeleportCandidateList => DetectTeleportCandidateList(imageRegion),
            ExperimentalTeleportUiState.TeleportDetails => DetectTeleportDetails(imageRegion),
            ExperimentalTeleportUiState.Loading => DetectLoading(imageRegion),
            _ => false,
        };
    }

    private static void ResetUnknownDetection(ExperimentalTeleportUiContext context)
    {
        context.UnknownDelayStarted = false;
        context.UnknownDetectedAt = 0;
        context.UnknownRecheckReady = false;
    }
}
