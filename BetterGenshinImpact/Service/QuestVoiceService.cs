using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.Element.Assets;

namespace BetterGenshinImpact.Service;

/// <summary>原生宿主适配：借用已有运行环境和截图，不接管输入或其他任务。</summary>
public sealed class QuestVoiceService : IDisposable
{
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
    /// <summary>保护显示快照。</summary>
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
    /// <summary>单调时钟起点及上次已接收帧。</summary>
    private long _startedAt, _lastCapture;
    /// <summary>限频计数摘要，下次输出的单调时刻；仅编码任务写入。</summary>
    private long _nextMetricsAt;
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
            lock (_captureGate) _startCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, runtime.Stopping);
            Update(s => s with { IsBusy = true, Session = "正在校验并启动", Error = "" });
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
            Update(s => s with { IsRunning = true, IsBusy = false, Session = "运行中", Ocr = "等待稳定对白",
                Tts = "等待首次合成", Capture = _config.CaptureMode + " · 复用已有帧；等待原神前台画面" });
            Diagnose("自动配音已启动；不会修改自动剧情设置或发送游戏输入。");
        }
        catch (Exception exception)
        {
            _overlay.StartupFailed();
            _logger.LogWarning(exception, "QuestVoice 启动失败");
            await _controller.StopAsync().ConfigureAwait(false);
            lock (_captureGate) { _startCancellation?.Dispose(); _startCancellation = null; }
            Update(s => s with { IsBusy = false, IsRunning = false, Session = "启动未完成", Error = exception.Message });
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
            encoding = _encoding;
        }
        var stop = _controller.StopAsync(abortBackend: applicationExit);
        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            Update(s => s with { IsRunning = false, IsBusy = true, Session = "停止中；等待已发出的后端请求收尾" });
            await Task.WhenAll(stop, encoding).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("QuestVoice final metrics {@Metrics}", _factory.Metrics.Snapshot());
            lock (_captureGate) { _startCancellation?.Dispose(); _startCancellation = null; }
            Update(s => s with { IsBusy = false, Session = "已停止", Ocr = "已停止", Playback = "已停止", Capture = "已停止采样" });
            Diagnose("自动配音已停止，所属后台工作已排空。");
            _overlay.Stopped();
        }
        catch (Exception exception)
        {
            Update(s => s with { IsRunning = false, IsBusy = false, Session = "停止时发生错误", Error = exception.Message });
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
            if (!Uri.TryCreate(_config.QuestVoiceConfig.ServiceUrl, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme is not ("http" or "https")) throw new InvalidOperationException("请先填写有效的 HTTP/HTTPS 后端地址。");
            using var client = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(3) })
                { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/docs"),
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            Update(s => s with { Tts = $"HTTP {(int)response.StatusCode} 已响应；实际合成待验证" });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { Update(s => s with { Tts = "连接检测失败", Error = exception.Message }); }
    }

    /// <summary>用户在手动恢复后明确确认；不把探测成功伪装成远端已空闲。</summary>
    public void ConfirmBackendRestarted()
    {
        if (Status.IsRunning || Status.IsBusy) throw new InvalidOperationException("请先停止自动配音。");
        Update(s => s with { BackendUnknown = false, Tts = "已确认后端重启；合成待验证", Error = "" });
    }

    /// <summary>OCR 状态来自实际事件，正文只显示正式接受的候选。</summary>
    private void OnFrameEvent(ContinuousFrameEvent value)
    {
        _overlay.OnFrame(value);
        if (value.Kind == ContinuousFrameEventKind.Observed && value.Inspection is { } inspection)
            Update(s => s with { Layout = inspection.Selection.Decision.LayoutId ?? inspection.Selection.Decision.Reason });
        else if (value.Kind == ContinuousFrameEventKind.OcrStarted) Update(s => s with { Ocr = "识别中" });
        else if (value.Kind == ContinuousFrameEventKind.OcrCompleted)
            Update(s => s with { Ocr = value.Detail is null ? "识别已返回" : "识别失败：" + value.Detail });
        else if (value.Kind == ContinuousFrameEventKind.CandidateAccepted && value.Ocr is { } ocr)
            Update(s => s with { Ocr = "已接受稳定对白", Character = ocr.Speaker ?? "未识别姓名", Dialogue = ocr.Dialogue });
        else if (value.Kind == ContinuousFrameEventKind.LeftDialogue) Update(s => s with { Ocr = "已离开对白" });
        else if (value.Kind == ContinuousFrameEventKind.Failed) Diagnose(value.Detail ?? "帧处理失败");
    }

    /// <summary>合成和播放独立显示，合成下一句不会清掉旧句播放状态。</summary>
    private void OnVoiceEvent(ContinuousVoiceEvent value)
    {
        _overlay.OnVoice(value);
        switch (value.Kind)
        {
            case ContinuousVoiceEventKind.Routed:
                var routing = value.Work?.Routing;
                Update(s => s with { Character = routing?.CanonicalName ?? value.Work?.Candidate.AcceptedName ?? "默认 NPC",
                    Voice = (routing?.Voice.Name ?? _config.QuestVoiceConfig.VoiceName)
                        + (routing?.FallbackReason is { } reason ? "（兜底：" + reason + "）" : "") });
                break;
            case ContinuousVoiceEventKind.SynthesisStarted: Update(s => s with { Tts = "切换权重 / 合成中" }); break;
            case ContinuousVoiceEventKind.SynthesisDraining: Update(s => s with { Tts = "候选已取消，正在排空旧响应" }); break;
            case ContinuousVoiceEventKind.AudioReady: Update(s => s with { Tts = "完整音频已就绪" }); break;
            case ContinuousVoiceEventKind.PlaybackStarted: Update(s => s with { Playback = "正在播放" }); break;
            case ContinuousVoiceEventKind.PlaybackCompleted:
                Update(s => s with { Playback = value.Error is null ? "播放结束" : "播放失败", Error = value.Error is null ? s.Error : value.Detail ?? "播放设备错误" }); break;
            case ContinuousVoiceEventKind.CandidateFailed:
                var unknown = value.Error is VoiceErrorCode.Timeout or VoiceErrorCode.InterruptedResponse or VoiceErrorCode.BackendUnknown;
                Update(s => s with { Tts = unknown ? "后端状态未知；请停止并重启后端" : "本句合成失败，等待下一句",
                    BackendUnknown = s.BackendUnknown || unknown, Error = value.Detail ?? "配音失败" });
                Diagnose(value.Detail ?? "配音失败");
                break;
        }
    }

    /// <summary>单帧错误可恢复，完整异常进入宿主日志。</summary>
    private void OnObservationFailed(Exception exception)
    {
        _overlay.ObservationFailed(exception);
        _logger.LogWarning(exception, "QuestVoice 单帧处理失败，继续等待下一帧");
        Update(s => s with { Ocr = "本帧失败，等待下一帧", Error = exception.Message });
        Diagnose(exception.Message);
    }
    /// <summary>不在核心锁里等待 UI；订阅者只收到不可变快照。</summary>
    private void Update(Func<QuestVoiceStatus, QuestVoiceStatus> change)
    {
        QuestVoiceStatus status;
        lock (_statusGate) { status = change(_status); if (status == _status) return; _status = status; }
        Changed?.Invoke(status);
    }
    /// <summary>保留最多八条简短诊断，详细记录使用宿主日志。</summary>
    private void Diagnose(string message)
    {
        string lines;
        lock (_statusGate)
        {
            _diagnostics.Enqueue($"{DateTime.Now:HH:mm:ss} {message}");
            while (_diagnostics.Count > 8) _diagnostics.Dequeue();
            lines = string.Join(Environment.NewLine, _diagnostics);
        }
        _logger.LogInformation("QuestVoice：{Message}", message);
        Update(s => s with { Diagnostics = lines });
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
