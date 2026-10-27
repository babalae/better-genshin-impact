using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BetterGenshinImpact.Service;

/// <summary>原生宿主适配：借用已有运行环境和截图，不接管输入或其他任务。</summary>
public sealed partial class QuestVoiceService : IDisposable
{
    /// <summary>最近事件列表最大保留条数。</summary>
    private const int MaxRecentEvents = 8;

    /// <summary>宿主与业务依赖，由 DI 统一拥有。</summary>
    private readonly GameRuntimeService _runtime;
    /// <summary>只订阅现有帧。</summary>
    private readonly TaskTriggerDispatcher _dispatcher;
    /// <summary>持久化设置根。</summary>
    private readonly AllConfig _config;
    /// <summary>单轮资源工厂。</summary>
    private readonly QuestVoiceSessionFactory _factory;
    /// <summary>异步资源所有者。</summary>
    private readonly QuestVoiceSessionController _controller;
    /// <summary>接入宿主日志。</summary>
    private readonly ILogger<QuestVoiceService> _logger;
    /// <summary>仅把真实业务事件投影为悬浮摘要，不改变页面快照或音频调度。</summary>
    private readonly QuestVoiceOverlayPresenter _overlay = new(QuestVoiceLog.WriteSummary);
    /// <summary>串行启停，不用于同步阻塞 UI。</summary>
    private readonly SemaphoreSlim _operations = new(1, 1);
    /// <summary>保护订阅、编码任务和单调采样时刻。</summary>
    private readonly object _captureGate = new();
    /// <summary>保护显示快照与本轮进度状态。</summary>
    private readonly object _statusGate = new();
    /// <summary>有限诊断行，避免独立复制日志系统。</summary>
    private readonly Queue<string> _diagnostics = new();
    /// <summary>本轮启动与观察取消源。</summary>
    private CancellationTokenSource? _startCancellation;
    /// <summary>归属于宿主的唯一编码任务。</summary>
    private Task _encoding = Task.CompletedTask;
    /// <summary>采样开关和本轮参数快照。</summary>
    private bool _sampling;
    /// <summary>在启动时冻结采样配置。</summary>
    private int _sampleInterval;
    /// <summary>在启动时冻结流式配音与角色路由开关。</summary>
    private bool _activeStreamingEnabled, _activeRoutingEnabled;
    /// <summary>当前引擎显示名冻结于启动时。</summary>
    private string _activeProviderName = "GPT-SoVITS";
    /// <summary>冻结实际默认声音；预设引擎显示其 voice ID，不借用旧 GPT 默认名。</summary>
    private string _activeVoiceName = "默认 NPC";
    /// <summary>单调时钟起点及上次已接收帧。</summary>
    private long _startedAt, _lastCapture;
    /// <summary>限频计数摘要，下次输出的单调时刻；仅编码任务写入。</summary>
    private long _nextMetricsAt;
    /// <summary>当前跟踪的对白周期与视觉版本，区分新旧对白轮次。</summary>
    private long _currentEpoch, _currentRevision;
    /// <summary>已观察到的语音代际；视觉重检不清除仍有效的语音工作。</summary>
    private long _currentSemanticGeneration;
    /// <summary>当前正式接受的候选身份（Epoch, Revision, SourceSequence）。</summary>
    private (long Epoch, long Revision, long Source)? _acceptedCandidate;
    /// <summary>当前正在播放的候选身份（Epoch, Revision, SourceSequence）。</summary>
    private (long Epoch, long Revision, long Source)? _playingCandidate;
    /// <summary>当前是否处于对白画面及是否有音频正在输出。</summary>
    private bool _inDialogue, _isPlayingAudio;
    /// <summary>最近一次记录的界面事件内容，避免稳定画面重复刷屏。</summary>
    private string? _lastEventText;
    /// <summary>线程间只发布完整快照。</summary>
    private QuestVoiceStatus _status = new();

    /// <summary>单例订阅一次运行环境，页面切换不会重建或停止会话。</summary>
    public QuestVoiceService(GameRuntimeService runtime, TaskTriggerDispatcher dispatcher, AllConfig config,
        QuestVoiceSessionFactory factory, QuestVoiceSessionController controller, ILogger<QuestVoiceService> logger)
    {
        _runtime = runtime; _dispatcher = dispatcher; _config = config;
        _factory = factory; _controller = controller; _logger = logger;
        _controller.FrameEvent += OnFrameEvent;
        _controller.VoiceEvent += OnVoiceEvent;
        _controller.ObservationFailed += OnObservationFailed;
        _runtime.Started += OnRuntimeStarted;
        _runtime.Stopped += OnRuntimeStopped;
        RefreshGame();
    }

    /// <summary>读取完整的真实状态快照。</summary>
    public QuestVoiceStatus Status { get { lock (_statusGate) return _status; } }
    /// <summary>回调可能来自工作线程，订阅方必须异步投递 UI。</summary>
    public event Action<QuestVoiceStatus>? Changed;

    /// <summary>必须借用已经启动的截图会话，避免隐式启动既有输入触发器。</summary>
    public async Task StartAsync(CancellationToken token = default)
    {
        await _operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_controller.IsRunning) return;
            if (Status.BackendUnknown) throw new InvalidOperationException("后端状态未知。请先手动重启 GPT-SoVITS，再点击“确认后端已重启”。");
            var runtime = _runtime.Current ?? throw new InvalidOperationException("请先在 BetterGI 启动页启用截图并绑定原神，再启动自动配音。");
            if (runtime.IsStopping || !runtime.Window.IsAlive) throw new InvalidOperationException("游戏窗口已失效，请重新绑定。");
            var ttsSettings = _factory.CreateTtsSettings();
            lock (_captureGate) _startCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, runtime.Stopping);
            lock (_statusGate)
            {
                _currentEpoch = 0;
                _currentRevision = 0;
                _currentSemanticGeneration = 0;
                _acceptedCandidate = null;
                _playingCandidate = null;
                _inDialogue = false;
                _isPlayingAudio = false;
                var info = TtsProviderRegistry.GetInfo(_config.QuestVoiceConfig.ProviderId);
                _activeStreamingEnabled = info.Capabilities.Streaming && _config.QuestVoiceConfig.StreamingEnabled;
                _activeRoutingEnabled = info.Capabilities.VoiceAssets && _config.QuestVoiceConfig.RoutingEnabled;
                _activeProviderName = info.DisplayName;
                _activeVoiceName = info.Capabilities.PresetVoices ? ttsSettings.Provider!.VoiceId : ttsSettings.Voice.Name;
                _lastEventText = null;
            }
            Update(s => ResetStages(s) with
            {
                IsBusy = true,
                Session = "正在校验并启动",
                OverallStatus = "等待对白",
                OverallStatusState = QuestVoiceStageState.InProgress,
                DialogueState = "正在启动",
                StageSummary = "当前阶段：正在校验配置并启动自动配音",
                Error = ""
            });
            _overlay.BeginSession();
            await _controller.StartAsync(_startCancellation.Token).ConfigureAwait(false);
            lock (_captureGate)
            {
                _startCancellation.Token.ThrowIfCancellationRequested();
                _sampleInterval = _config.QuestVoiceConfig.SampleIntervalMilliseconds;
                _startedAt = Stopwatch.GetTimestamp();
                _lastCapture = 0;
                _nextMetricsAt = _startedAt + 10 * Stopwatch.Frequency;
                _dispatcher.FrameCaptured += OnCaptured;
                _sampling = true;
            }
            Update(s => ResetStages(s) with
            {
                IsRunning = true,
                IsBusy = false,
                Session = "运行中",
                OverallStatus = "等待对白",
                OverallStatusState = QuestVoiceStageState.InProgress,
                DialogueState = "等待对白",
                StageSummary = "当前阶段：等待检测到原神对话界面",
                Ocr = "等待稳定对白",
                Tts = "等待首次合成",
                Playback = "空闲",
                Character = "—",
                Voice = "—",
                Dialogue = "尚未识别到稳定对白",
                Capture = _config.CaptureMode + " · 复用已有帧；等待原神前台画面"
            });
            Diagnose("自动配音已启动");
        }
        catch (Exception exception)
        {
            _overlay.StartupFailed();
            _logger.LogWarning(exception, "QuestVoice 启动失败");
            await _controller.StopAsync().ConfigureAwait(false);
            lock (_captureGate) { _startCancellation?.Dispose(); _startCancellation = null; }
            Update(s => ResetStages(s) with
            {
                IsBusy = false,
                IsRunning = false,
                Session = "启动未完成",
                OverallStatus = "异常",
                OverallStatusState = QuestVoiceStageState.Failed,
                DialogueState = "未运行",
                StageSummary = "当前阶段：启动未完成",
                Error = exception.Message
            });
            throw;
        }
        finally { _operations.Release(); }
    }

    /// <summary>停止立即取消采样/OCR/播放，已发出的 HTTP 保留串行排空策略。</summary>
    public async Task StopAsync(bool applicationExit = false)
    {
        _overlay.StopRequested();
        Task encoding;
        lock (_captureGate)
        {
            _sampling = false;
            _dispatcher.FrameCaptured -= OnCaptured;
            _startCancellation?.Cancel();
            _testCancellation?.Cancel();
            if (applicationExit) _testProvider?.Shutdown();
            encoding = _encoding;
        }
        var stop = _controller.StopAsync(abortBackend: applicationExit);
        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            Update(s => s with
            {
                IsRunning = false,
                IsBusy = true,
                Session = "停止中；等待已发出的后端请求收尾",
                StageSummary = "当前阶段：正在停止并排空已发出的请求"
            });
            await Task.WhenAll(stop, encoding).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("QuestVoice final metrics {@Metrics}", _factory.Metrics.Snapshot());
            lock (_captureGate) { _startCancellation?.Dispose(); _startCancellation = null; }
            lock (_statusGate)
            {
                _inDialogue = false;
                _isPlayingAudio = false;
                _acceptedCandidate = null;
                _playingCandidate = null;
            }
            Update(s => ResetStages(s) with
            {
                IsBusy = false,
                IsRunning = false,
                Session = "已停止",
                OverallStatus = s.BackendUnknown || !string.IsNullOrEmpty(s.Error) ? "异常" : "已停止",
                OverallStatusState = s.BackendUnknown || !string.IsNullOrEmpty(s.Error) ? QuestVoiceStageState.Failed : QuestVoiceStageState.Pending,
                DialogueState = "未运行",
                StageSummary = "当前阶段：已停止",
                Ocr = "已停止",
                Playback = "已停止",
                Capture = "已停止采样"
            });
            Diagnose("自动配音已停止");
            _overlay.Stopped();
        }
        catch (Exception exception)
        {
            Update(s => s with
            {
                IsRunning = false,
                IsBusy = false,
                Session = "停止时发生错误",
                OverallStatus = "异常",
                OverallStatusState = QuestVoiceStageState.Failed,
                StageSummary = "当前阶段：停止时发生错误",
                Error = exception.Message
            });
            _overlay.StopFailed();
            _logger.LogError(exception, "QuestVoice 停止时发生错误");
            throw;
        }
        finally { _operations.Release(); }
    }

    /// <summary>只复制必要帧；不在调度线程编码、OCR、等待任务或调用 UI。</summary>
    private void OnCaptured(Mat frame)
    {
        lock (_captureGate)
        {
            // 退订前已被宿主取得的旧回调可能迟到；停止后不再改变本轮最终计数。
            if (!_sampling) return;
            var metrics = _factory.Metrics;
            metrics.Record(QuestVoiceMetric.ReceivedFrame);
            var now = Stopwatch.GetTimestamp();
            if (!_encoding.IsCompleted || _controller.IsObserving
                || (_lastCapture != 0 && Stopwatch.GetElapsedTime(_lastCapture, now).TotalMilliseconds < _sampleInterval))
            { metrics.Record(QuestVoiceMetric.FilteredFrame); return; }
            var cloneStart = Stopwatch.GetTimestamp();
            var copy = frame.Clone();
            metrics.Record(QuestVoiceMetric.Clone, Stopwatch.GetTimestamp() - cloneStart);
            _lastCapture = now;
            var at = Stopwatch.GetElapsedTime(_startedAt, now);
            _encoding = Task.Run(() =>
            {
                using (copy)
                {
                    try
                    {
                        var encodeStart = Stopwatch.GetTimestamp();
                        var bytes = copy.ImEncode(".bmp");
                        metrics.Record(QuestVoiceMetric.Encode, Stopwatch.GetTimestamp() - encodeStart);
                        if (_controller.TryObserve(bytes, at, HasConfirmedExitEvidence(copy))) Update(s => s with { Capture = $"已采样 {copy.Width}×{copy.Height} · {_sampleInterval} ms" });
                        var completedAt = Stopwatch.GetTimestamp();
                        if (completedAt >= _nextMetricsAt)
                        {
                            _nextMetricsAt = completedAt + 10 * Stopwatch.Frequency;
                            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("QuestVoice metrics {@Metrics}", metrics.Snapshot());
                        }
                    }
                    catch (Exception exception) { OnObservationFailed(exception); }
                }
            });
        }
    }

    /// <summary>只在后台借用当前复制帧；模板失败表示未知，绝不推定退出。</summary>
    private bool HasConfirmedExitEvidence(Mat frame)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            // 区域仅拥有新的 Mat 头，不拥有外层 copy 的生命周期，也不再复制像素。
            using var capture = new GameCaptureRegion(new Mat(frame, new Rect(0, 0, frame.Width, frame.Height)), 0, 0);
            using var region = capture.DeriveTo1080P();
            // Bv.IsInMainUi包含复苏弹窗OCR；此处仅复用其正向HUD模板，避免引入另一条OCR路径。
            using var main = region.Find(ElementRecognition.Get("PaimonMenu", region));
            if (!main.IsExist()) return false;
            using var confirm = region.Find(RecognitionAssets.Get("AutoFight", "Confirm", region));
            return confirm.IsEmpty() && !Bv.IsInTalkUi(region);
        }
        catch (Exception) { return false; }
        finally { _factory.Metrics.Record(QuestVoiceMetric.ExitEvidence, Stopwatch.GetTimestamp() - start); }
    }

    /// <summary>静态资源和路由校验运行在工作线程，不阻塞窗口。</summary>
    public async Task ValidateAsync(CancellationToken token = default)
    {
        var result = await Task.Run(() => _factory.ValidateAsync(token), token).ConfigureAwait(false);
        Update(s => s with { Validation = result.Detail, Routing = result.RoutingSummary });
    }

    /// <summary>只做只读 HTTP 可达性探测，成功不意味着权重或合成已经可用。</summary>
    public async Task ProbeBackendAsync(CancellationToken token = default)
    {
        try
        {
            var settings = _factory.CreateTtsSettings();
            using var provider = TtsProviderRegistry.Create(settings.Provider!.ProviderId);
            var detail = await provider.ProbeAsync(settings, token).ConfigureAwait(false);
            Update(s => s with { Tts = detail, Error = "" });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            Update(s => s with
            {
                Tts = "连接检测失败",
                Error = exception.Message,
                OverallStatus = "异常",
                OverallStatusState = QuestVoiceStageState.Failed
            });
        }
    }

    /// <summary>用户在手动恢复后明确确认；不把探测成功伪装成远端已空闲。</summary>
    public void ConfirmBackendRestarted()
    {
        if (Status.IsRunning || Status.IsBusy) throw new InvalidOperationException("请先停止自动配音。");
        Update(s => s with
        {
            BackendUnknown = false,
            Tts = "已确认后端重启；合成待验证",
            Error = "",
            OverallStatus = "已停止",
            OverallStatusState = QuestVoiceStageState.Pending
        });
    }

    /// <summary>OCR 状态来自实际事件，正文只显示正式接受的候选。</summary>
    private void OnFrameEvent(ContinuousFrameEvent value)
    {
        _overlay.OnFrame(value);
        switch (value.Kind)
        {
            case ContinuousFrameEventKind.Started:
                Update(s => ResetStages(s) with
                {
                    OverallStatus = "等待对白",
                    OverallStatusState = QuestVoiceStageState.InProgress,
                    DialogueState = "等待对白",
                    StageSummary = "当前阶段：等待检测到原神对话界面"
                });
                break;

            case ContinuousFrameEventKind.Observed when value.Inspection is { } inspection:
                var layoutText = inspection.Selection.Decision.LayoutId ?? inspection.Selection.Decision.Reason;
                var isSelected = inspection.Selection.Decision.Kind == SceneDecisionKind.Selected;
                var enteredDialogue = false;
                var hasAcceptedCurrent = false;
                lock (_statusGate)
                {
                    if (!AdvanceVersion(value.Epoch, value.Revision, value.SemanticGeneration)) return;
                    if (isSelected && !_inDialogue)
                    {
                        _inDialogue = true;
                        enteredDialogue = true;
                    }
                    hasAcceptedCurrent = _acceptedCandidate is { } acc
                        && acc.Epoch == value.Epoch;
                }
                if (enteredDialogue) RecordEvent("检测到对话界面");
                Update(s =>
                {
                    var next = s with { Layout = layoutText };
                    if (isSelected && !hasAcceptedCurrent && next.OcrStage == QuestVoiceStageState.Pending)
                    {
                        next = next with
                        {
                            OverallStatus = string.IsNullOrEmpty(next.Error) ? "运行中" : "异常",
                            OverallStatusState = string.IsNullOrEmpty(next.Error) ? QuestVoiceStageState.Completed : QuestVoiceStageState.Failed,
                            SceneStage = QuestVoiceStageState.Completed,
                            DialogueState = "已检测到对话界面（等待对白稳定）",
                            StageSummary = "当前阶段：已检测到对话界面，等待对白稳定"
                        };
                    }
                    return next;
                });
                break;

            case ContinuousFrameEventKind.Invalidated:
                var inDialogueNow = false;
                var playingNow = false;
                lock (_statusGate)
                {
                    if (!AdvanceVersion(value.Epoch, value.Revision, value.SemanticGeneration)) return;
                    // 普通画面变化仅重做观察/OCR，不回退正在合成或播放的语音阶段。
                    if (_acceptedCandidate is not null) break;
                    inDialogueNow = _inDialogue;
                    playingNow = _isPlayingAudio;
                }
                Update(s => s with
                {
                    SceneStage = inDialogueNow ? QuestVoiceStageState.Completed : QuestVoiceStageState.Pending,
                    OcrStage = QuestVoiceStageState.Pending,
                    SpeakerStage = QuestVoiceStageState.Pending,
                    RoutingStage = QuestVoiceStageState.Pending,
                    TtsStage = QuestVoiceStageState.Pending,
                    PlaybackStage = playingNow ? QuestVoiceStageState.InProgress : QuestVoiceStageState.Pending,
                    DialogueState = inDialogueNow ? "对白更新，等待文字稳定" : "等待对白",
                    StageSummary = inDialogueNow ? "当前阶段：检测到对白变化，等待文字稳定" : "当前阶段：等待检测到原神对话界面"
                });
                break;

            case ContinuousFrameEventKind.OcrStarted:
                var skipRecheck = false;
                var playingDuringOcr = false;
                lock (_statusGate)
                {
                    if (!AdvanceVersion(value.Epoch, value.Revision, value.SemanticGeneration)) return;
                    _inDialogue = true;
                    playingDuringOcr = _isPlayingAudio;
                    // 已接受语音身份跨视觉重检保持有效，不回退流程节点。
                    skipRecheck = _acceptedCandidate is { } acc
                        && acc.Epoch == value.Epoch;
                }
                if (!skipRecheck)
                {
                    Update(s => s with
                    {
                        OverallStatus = string.IsNullOrEmpty(s.Error) ? "运行中" : "异常",
                        OverallStatusState = string.IsNullOrEmpty(s.Error) ? QuestVoiceStageState.Completed : QuestVoiceStageState.Failed,
                        SceneStage = QuestVoiceStageState.Completed,
                        OcrStage = QuestVoiceStageState.InProgress,
                        SpeakerStage = QuestVoiceStageState.Pending,
                        RoutingStage = QuestVoiceStageState.Pending,
                        TtsStage = QuestVoiceStageState.Pending,
                        PlaybackStage = playingDuringOcr ? QuestVoiceStageState.InProgress : QuestVoiceStageState.Pending,
                        Ocr = "识别中",
                        DialogueState = "正在识别对白",
                        StageSummary = "当前阶段：正在进行 OCR 识别"
                    });
                }
                break;

            case ContinuousFrameEventKind.OcrCompleted:
                var isAcceptedRevision = false;
                lock (_statusGate)
                {
                    if (IsStale(value.Epoch, value.Revision)) return;
                    isAcceptedRevision = _acceptedCandidate is { } acc
                        && acc.Epoch == value.Epoch;
                }
                if (value.Detail is not null)
                {
                    Update(s => s with
                    {
                        Ocr = "识别失败：" + value.Detail,
                        OcrStage = QuestVoiceStageState.Failed,
                        DialogueState = "OCR 识别异常",
                        StageSummary = "当前阶段：OCR 识别失败"
                    });
                }
                else if (!isAcceptedRevision)
                {
                    Update(s => s with { Ocr = "识别已返回" });
                }
                break;

            case ContinuousFrameEventKind.CandidateAccepted when value.Ocr is { } ocr:
                var sourceSeq = value.SourceSequence ?? -1;
                var playingWhenAccepted = false;
                lock (_statusGate)
                {
                    if (!AdvanceVersion(value.Epoch, value.Revision, value.SemanticGeneration)) return;
                    var identity = (value.Epoch, value.Revision, sourceSeq);
                    if (_acceptedCandidate == identity) return;
                    _acceptedCandidate = identity;
                    _inDialogue = true;
                    playingWhenAccepted = _isPlayingAudio;
                }
                var speakerName = string.IsNullOrWhiteSpace(ocr.Speaker) ? "未识别姓名" : ocr.Speaker.Trim();
                Update(s => s with
                {
                    OverallStatus = "运行中",
                    OverallStatusState = QuestVoiceStageState.Completed,
                    SceneStage = QuestVoiceStageState.Completed,
                    OcrStage = QuestVoiceStageState.Completed,
                    SpeakerStage = QuestVoiceStageState.Completed,
                    RoutingStage = QuestVoiceStageState.InProgress,
                    TtsStage = QuestVoiceStageState.Pending,
                    PlaybackStage = playingWhenAccepted ? QuestVoiceStageState.InProgress : QuestVoiceStageState.Pending,
                    DialogueState = "已检测到稳定对白",
                    Ocr = "已接受稳定对白",
                    Character = speakerName,
                    Dialogue = ocr.Dialogue,
                    Voice = "正在匹配声线...",
                    StageSummary = "当前阶段：OCR 与角色识别完成，正在匹配声线路由",
                    Error = ""
                });
                RecordEvent("OCR 识别成功");
                RecordEvent($"识别角色：{speakerName}");
                break;

            case ContinuousFrameEventKind.LeftDialogue:
                HandleLeftDialogue(value.Epoch, value.Revision, value.SemanticGeneration);
                break;

            case ContinuousFrameEventKind.Failed:
                Update(s => s with
                {
                    OverallStatus = "异常",
                    OverallStatusState = QuestVoiceStageState.Failed,
                    SceneStage = s.OcrStage == QuestVoiceStageState.InProgress ? s.SceneStage : QuestVoiceStageState.Failed,
                    OcrStage = s.OcrStage == QuestVoiceStageState.InProgress ? QuestVoiceStageState.Failed : s.OcrStage,
                    DialogueState = "识别异常",
                    StageSummary = "当前阶段：画面或 OCR 识别失败"
                });
                Diagnose(value.Detail ?? "帧处理失败");
                break;
        }
    }

    /// <summary>合成和播放独立显示，合成下一句不会清掉旧句播放状态。</summary>
    private void OnVoiceEvent(ContinuousVoiceEvent value)
    {
        _overlay.OnVoice(value);
        if (value.Kind == ContinuousVoiceEventKind.LeftDialogue)
        {
            HandleLeftDialogue(value.Epoch, null, value.Revision);
            return;
        }

        var candidate = value.Work?.Candidate;
        var candidateId = candidate is null
            ? ((long Epoch, long Revision, long Source)?)null
            : (candidate.Epoch, candidate.Revision, candidate.SourceSequence);

        if (value.Kind == ContinuousVoiceEventKind.PlaybackCompleted)
        {
            var isCurrentAccepted = false;
            lock (_statusGate)
            {
                if (candidateId is not null && _playingCandidate == candidateId)
                {
                    _isPlayingAudio = false;
                    _playingCandidate = null;
                }
                isCurrentAccepted = candidateId is not null && _acceptedCandidate == candidateId;
            }
            if (value.Error is not null)
            {
                Update(s => s with
                {
                    OverallStatus = "异常",
                    OverallStatusState = QuestVoiceStageState.Failed,
                    PlaybackStage = QuestVoiceStageState.Failed,
                    Playback = "播放失败",
                    StageSummary = "当前阶段：音频播放异常",
                    Error = value.Detail ?? "播放设备错误"
                });
                Diagnose(value.Detail ?? "播放设备错误");
            }
            else if (isCurrentAccepted)
            {
                Update(s => s with
                {
                    PlaybackStage = value.PlaybackSucceeded ? QuestVoiceStageState.Completed : QuestVoiceStageState.Pending,
                    Playback = value.PlaybackSucceeded ? "播放结束" : "已停止",
                    StageSummary = value.PlaybackSucceeded ? "当前阶段：本句配音播放完成，等待下一句对白" : s.StageSummary
                });
            }
            else
            {
                // 旧句播放结束时，若新句尚未开始播放，则将播放状态归位为等待播放音频。
                Update(s => s.PlaybackStage == QuestVoiceStageState.InProgress
                    ? s with { PlaybackStage = QuestVoiceStageState.Pending, Playback = "等待播放音频" }
                    : s);
            }
            return;
        }

        if (value.Kind == ContinuousVoiceEventKind.PlaybackStopFailed)
        {
            Update(s => s with
            {
                OverallStatus = "异常",
                OverallStatusState = QuestVoiceStageState.Failed,
                PlaybackStage = QuestVoiceStageState.Failed,
                Playback = "播放失败",
                StageSummary = "当前阶段：音频播放停止异常",
                Error = value.Detail ?? "播放设备错误"
            });
            Diagnose(value.Detail ?? "播放设备错误");
            return;
        }

        bool isCurrentCandidate;
        bool streamingMode;
        bool routingMode;
        bool playingAudio;
        lock (_statusGate)
        {
            isCurrentCandidate = candidateId is not null && _acceptedCandidate == candidateId;
            streamingMode = _activeStreamingEnabled;
            routingMode = _activeRoutingEnabled;
            playingAudio = _isPlayingAudio;
        }
        if (!isCurrentCandidate) return;

        switch (value.Kind)
        {
            case ContinuousVoiceEventKind.RoutingStarted:
                Update(s => s with
                {
                    RoutingStage = QuestVoiceStageState.InProgress,
                    StageSummary = "当前阶段：正在解析角色声线路由"
                });
                break;

            case ContinuousVoiceEventKind.Routed:
                var routing = value.Work?.Routing;
                var characterName = routing?.CanonicalName ?? value.Work?.Candidate.AcceptedName ?? "默认 NPC";
                var voiceDisplay = FormatRoutedVoice(routing);
                var voiceShortName = routing?.Voice.Name ?? _activeVoiceName;
                Update(s => s with
                {
                    Character = characterName,
                    Voice = voiceDisplay,
                    SpeakerStage = QuestVoiceStageState.Completed,
                    RoutingStage = QuestVoiceStageState.Completed,
                    StageSummary = $"当前阶段：声线路由完成（{voiceDisplay}）"
                });
                RecordEvent($"声线路由命中：{voiceShortName}");
                break;

            case ContinuousVoiceEventKind.Duplicate:
                Update(s => s with
                {
                    RoutingStage = QuestVoiceStageState.Completed,
                    TtsStage = QuestVoiceStageState.Completed,
                    PlaybackStage = QuestVoiceStageState.Completed,
                    Tts = "同句已播，跳过重复合成",
                    StageSummary = "当前阶段：当前对白已播放，等待下一句对白"
                });
                break;

            case ContinuousVoiceEventKind.SynthesisStarted:
                Update(s => s with
                {
                    RoutingStage = QuestVoiceStageState.Completed,
                    TtsStage = QuestVoiceStageState.InProgress,
                    Tts = streamingMode ? "流式合成中" : "合成中",
                    Playback = playingAudio ? s.Playback : "等待播放音频",
                    StageSummary = streamingMode
                        ? "当前阶段：正在等待流式音频首段返回"
                        : $"当前阶段：正在请求 {_activeProviderName} 合成语音"
                });
                RecordEvent($"开始 {_activeProviderName} 合成");
                break;

            case ContinuousVoiceEventKind.SynthesisDraining:
                Update(s => s with
                {
                    TtsStage = QuestVoiceStageState.InProgress,
                    Tts = "候选已取消，正在排空旧响应",
                    StageSummary = "当前阶段：正在排空上一条后端合成响应"
                });
                break;

            case ContinuousVoiceEventKind.AudioReady:
                var isStreamingReady = value.Detail is not null;
                Update(s => s with
                {
                    // 流式首段就绪时后续音频仍在接收，因此保持 TTS 合成进行中，允许与播放重叠。
                    TtsStage = isStreamingReady ? QuestVoiceStageState.InProgress : QuestVoiceStageState.Completed,
                    Tts = value.Detail ?? "完整音频已就绪",
                    StageSummary = isStreamingReady
                        ? "当前阶段：流式音频首段已就绪，准备播放并继续接收后续音频"
                        : "当前阶段：语音合成完成，等待开始播放"
                });
                if (isStreamingReady) RecordEvent("等待流式音频片段");
                break;

            case ContinuousVoiceEventKind.SynthesisCompleted:
                Update(s => s with
                {
                    TtsStage = QuestVoiceStageState.Completed,
                    Tts = value.Detail ?? "合成完成",
                    StageSummary = s.PlaybackStage == QuestVoiceStageState.InProgress
                        ? "当前阶段：语音合成已完成，正在播放音频"
                        : "当前阶段：语音合成已完成，等待播放音频"
                });
                break;

            case ContinuousVoiceEventKind.PlaybackStarted:
                lock (_statusGate)
                {
                    _isPlayingAudio = true;
                    _playingCandidate = candidateId;
                }
                Update(s => s with
                {
                    PlaybackStage = QuestVoiceStageState.InProgress,
                    Playback = "正在播放",
                    StageSummary = s.TtsStage == QuestVoiceStageState.InProgress
                        ? "当前阶段：流式合成与音频播放同时进行中"
                        : "当前阶段：正在播放音频"
                });
                RecordEvent("开始播放音频");
                break;

            case ContinuousVoiceEventKind.CandidateFailed:
                var unknown = value.BackendUnknown;
                var failedDuringRouting = value.Work?.Routing is null && routingMode;
                Update(s => s with
                {
                    OverallStatus = "异常",
                    OverallStatusState = QuestVoiceStageState.Failed,
                    RoutingStage = failedDuringRouting ? QuestVoiceStageState.Failed : s.RoutingStage,
                    TtsStage = failedDuringRouting ? s.TtsStage : QuestVoiceStageState.Failed,
                    Tts = unknown ? "后端状态未知；请停止并重启后端" : "本句合成失败，等待下一句",
                    BackendUnknown = s.BackendUnknown || unknown,
                    StageSummary = unknown
                        ? "当前阶段：合成服务状态未知，请停止配音并检查后端"
                        : "当前阶段：本句语音合成失败，等待下一句",
                    Error = value.Detail ?? "配音失败"
                });
                Diagnose(value.Detail ?? "配音失败");
                break;
        }
    }

    /// <summary>单帧错误可恢复，完整异常进入宿主日志。</summary>
    private void OnObservationFailed(Exception exception)
    {
        _overlay.ObservationFailed(exception);
        _logger.LogWarning(exception, "QuestVoice 单帧处理失败，继续等待下一帧");
        Update(s => s with
        {
            OverallStatus = "异常",
            OverallStatusState = QuestVoiceStageState.Failed,
            OcrStage = QuestVoiceStageState.Failed,
            DialogueState = "单帧识别异常",
            StageSummary = "当前阶段：本帧识别异常，继续等待下一帧",
            Ocr = "本帧失败，等待下一帧",
            Error = exception.Message
        });
        Diagnose(exception.Message);
    }

    /// <summary>处理确认离开对白界面时的状态复位。</summary>
    private void HandleLeftDialogue(long epoch, long? revision, long semanticGeneration)
    {
        var wasInDialogue = false;
        lock (_statusGate)
        {
            if (epoch < _currentEpoch) return;
            _currentEpoch = epoch;
            if (revision is { } visualRevision) _currentRevision = visualRevision;
            _currentSemanticGeneration = Math.Max(_currentSemanticGeneration, semanticGeneration);
            wasInDialogue = _inDialogue;
            _inDialogue = false;
            _acceptedCandidate = null;
        }
        Update(s => ResetStages(s) with
        {
            OverallStatus = s.IsRunning && string.IsNullOrEmpty(s.Error) && !s.BackendUnknown ? "等待对白" : s.OverallStatus,
            OverallStatusState = s.IsRunning && string.IsNullOrEmpty(s.Error) && !s.BackendUnknown
                ? QuestVoiceStageState.InProgress : s.OverallStatusState,
            DialogueState = s.IsRunning ? "等待对白" : "未运行",
            Ocr = "已离开对白",
            StageSummary = "当前阶段：已离开对话界面，等待下一段对白"
        });
        if (wasInDialogue) RecordEvent("已离开对话界面");
    }

    /// <summary>生成实际声线路由展示文本，明确区分映射命中、固定默认声线与兜底原因。</summary>
    private string FormatRoutedVoice(VoiceRoutingResult? routing) =>
        QuestVoiceStatus.FormatRoutedVoice(routing, _activeVoiceName);

    /// <summary>检查事件是否早于当前已推进的对白周期和视觉版本。</summary>
    private bool IsStale(long epoch, long revision) =>
        epoch < _currentEpoch || (epoch == _currentEpoch && revision < _currentRevision);

    /// <summary>推进周期与视觉版本；若进入新版本则清理上一版本的候选标记。</summary>
    private bool AdvanceVersion(long epoch, long revision, long semanticGeneration)
    {
        if (IsStale(epoch, revision)) return false;
        if (epoch != _currentEpoch || semanticGeneration > _currentSemanticGeneration)
        {
            _acceptedCandidate = null;
        }
        _currentEpoch = epoch;
        _currentRevision = revision;
        _currentSemanticGeneration = semanticGeneration;
        return true;
    }

    /// <summary>重置六个阶段节点为尚未开始状态。</summary>
    private static QuestVoiceStatus ResetStages(QuestVoiceStatus status) => status with
    {
        SceneStage = QuestVoiceStageState.Pending,
        OcrStage = QuestVoiceStageState.Pending,
        SpeakerStage = QuestVoiceStageState.Pending,
        RoutingStage = QuestVoiceStageState.Pending,
        TtsStage = QuestVoiceStageState.Pending,
        PlaybackStage = QuestVoiceStageState.Pending
    };

    /// <summary>不在核心锁里等待 UI；订阅者只收到不可变快照。</summary>
    private void Update(Func<QuestVoiceStatus, QuestVoiceStatus> change)
    {
        QuestVoiceStatus status;
        lock (_statusGate) { status = change(_status); if (status == _status) return; _status = status; }
        Changed?.Invoke(status);
    }

    /// <summary>记录简短界面反馈事件（最多保留 8 条），不重复写入宿主文件日志。</summary>
    private void RecordEvent(string message)
    {
        string lines;
        string[] snapshot;
        lock (_statusGate)
        {
            if (_lastEventText == message) return;
            _lastEventText = message;
            _diagnostics.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
            while (_diagnostics.Count > MaxRecentEvents) _diagnostics.Dequeue();
            snapshot = _diagnostics.ToArray();
            lines = string.Join(Environment.NewLine, snapshot);
        }
        Update(s => s with { Diagnostics = lines, RecentEvents = snapshot });
    }

    /// <summary>保留最多八条简短诊断，并将重要生命周期或故障同步写入宿主日志。</summary>
    private void Diagnose(string message)
    {
        RecordEvent(message);
        _logger.LogInformation("QuestVoice：{Message}", message);
    }

    /// <summary>绑定状态来自宿主，不猜测游戏进程。</summary>
    private void RefreshGame() => Update(s => s with { Game = _runtime.Current is { } runtime && !runtime.IsStopping
        ? $"已绑定 · PID {runtime.Window.ProcessId}" : "未绑定游戏；请先在启动页启用截图" });
    /// <summary>运行环境事件只更新实际绑定状态。</summary>
    private void OnRuntimeStarted(object? sender, EventArgs e) => RefreshGame();
    /// <summary>窗口失效时自动排空，后台故障必须被观察。</summary>
    private async void OnRuntimeStopped(object? sender, EventArgs e)
    {
        _overlay.Disconnected();
        RefreshGame();
        try { await StopAsync().ConfigureAwait(false); }
        catch (Exception exception) { _logger.LogError(exception, "QuestVoice 游戏解绑后收尾失败"); }
    }
    /// <summary>App.OnExit 已异步排空；此处只移除进程级事件。</summary>
    public void Dispose()
    {
        _dispatcher.FrameCaptured -= OnCaptured;
        _runtime.Started -= OnRuntimeStarted;
        _runtime.Stopped -= OnRuntimeStopped;
        _controller.FrameEvent -= OnFrameEvent;
        _controller.VoiceEvent -= OnVoiceEvent;
        _controller.ObservationFailed -= OnObservationFailed;
    }
}
