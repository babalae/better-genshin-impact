using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 原生宿主适配：借用已有运行环境和截图，不接管输入或其他任务。
/// 启停周期与实时触发器一致：开关打开且截图器运行时启动；独立任务、脚本或设置窗口打开期间停止；截图器停止时停止。
/// </summary>
public sealed partial class QuestVoiceService : IDisposable
{
    /// <summary>宿主与业务依赖，由 DI 统一拥有。</summary>
    private readonly GameRuntimeService _runtime;
    /// <summary>只订阅现有帧，并读取任务模式。</summary>
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
    /// <summary>串行启停与试听，不用于同步阻塞 UI。</summary>
    private readonly SemaphoreSlim _operations = new(1, 1);
    /// <summary>保护订阅、编码任务和单调采样时刻。</summary>
    private readonly object _captureGate = new();
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
    /// <summary>正在打开的设置窗口数量；大于 0 时暂停配音。</summary>
    private int _settingsEditors;
    /// <summary>应用退出后不再启动新会话。</summary>
    private volatile bool _exiting;

    /// <summary>单例订阅运行环境、开关和任务模式，页面不拥有配音生命周期。</summary>
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
        _dispatcher.TaskModeChanged += OnTaskModeChanged;
        _config.QuestVoiceConfig.PropertyChanged += OnConfigChanged;
        RefreshGame();
        RequestApply();
    }

    /// <summary>
    /// 是否应当运行：开关打开、截图器运行中、不在独立任务中、设置窗口未打开且应用未退出。
    /// </summary>
    private bool ShouldRun => !_exiting && _config.QuestVoiceConfig.Enabled
        && _runtime.Current is { IsStopping: false } && !_dispatcher.IsInTask && Volatile.Read(ref _settingsEditors) == 0;

    /// <summary>打开设置窗口时调用；释放返回值后按新设置恢复。窗口打开期间停止配音，便于试听和修改。</summary>
    public IDisposable BeginSettingsEdit()
    {
        Interlocked.Increment(ref _settingsEditors);
        RequestApply();
        return new SettingsEditLease(this);
    }

    /// <summary>应用退出时调用：立即中止采样和后端请求，并等待本地资源释放。</summary>
    public async Task StopAsync(bool applicationExit = false)
    {
        if (applicationExit) _exiting = true;
        var stopping = BeginStop(applicationExit);
        await _operations.WaitAsync().ConfigureAwait(false);
        try { await FinishStopAsync(stopping).ConfigureAwait(false); }
        finally { _operations.Release(); }
    }

    /// <summary>状态变化后按期望状态启停；不需要运行时立即取消正在进行的启动。</summary>
    private void RequestApply()
    {
        if (!ShouldRun)
        {
            lock (_captureGate) _startCancellation?.Cancel();
        }
        _ = ApplyDesiredStateAsync();
    }

    /// <summary>按期望状态串行启停会话；等待期间状态再次变化则继续处理。</summary>
    private async Task ApplyDesiredStateAsync()
    {
        try
        {
            await _operations.WaitAsync().ConfigureAwait(false);
            try
            {
                while (ShouldRun != _controller.IsRunning)
                {
                    if (_controller.IsRunning)
                    {
                        await FinishStopAsync(BeginStop(applicationExit: false)).ConfigureAwait(false);
                    }
                    else if (!await TryStartCoreAsync().ConfigureAwait(false))
                    {
                        // 启动失败只显示错误，等下一次事件再试，避免反复重试。
                        break;
                    }
                }
                UpdateIdleStatus();
            }
            finally { _operations.Release(); }
        }
        catch (Exception exception) { _logger.LogError(exception, "QuestVoice 启停处理失败"); }
    }

    /// <summary>在持有 _operations 时创建会话并开始采样；失败只更新状态，不向外抛出。</summary>
    private async Task<bool> TryStartCoreAsync()
    {
        var runtime = _runtime.Current;
        if (runtime is null || runtime.IsStopping || !runtime.Window.IsAlive) return false;
        if (Status.BackendUnknown)
        {
            Update(s => s with { Session = "后端状态未知，等待确认", Error = "后端状态未知。请先手动重启 GPT-SoVITS，再点击“确认后端已重启”。" });
            return false;
        }
        try
        {
            var ttsSettings = _factory.CreateTtsSettings();
            lock (_captureGate) _startCancellation = CancellationTokenSource.CreateLinkedTokenSource(runtime.Stopping);
            ResetProjection();
            var info = TtsProviderRegistry.GetInfo(_config.QuestVoiceConfig.ProviderId);
            lock (_statusGate)
            {
                _activeStreamingEnabled = info.Capabilities.Streaming && _config.QuestVoiceConfig.StreamingEnabled;
                _activeRoutingEnabled = info.Capabilities.VoiceAssets && _config.QuestVoiceConfig.RoutingEnabled;
                _activeProviderName = info.DisplayName;
                _activeVoiceName = info.Capabilities.PresetVoices ? ttsSettings.Provider!.VoiceId : ttsSettings.Voice.Name;
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
            return true;
        }
        catch (Exception exception)
        {
            var cancelled = exception is OperationCanceledException;
            if (cancelled) _overlay.Stopped();
            else
            {
                _overlay.StartupFailed();
                _logger.LogWarning(exception, "QuestVoice 启动失败");
            }
            await _controller.StopAsync().ConfigureAwait(false);
            lock (_captureGate) { _startCancellation?.Dispose(); _startCancellation = null; }
            Update(s => ResetStages(s) with
            {
                IsBusy = false,
                IsRunning = false,
                Session = cancelled ? "已停止" : "启动未完成",
                OverallStatus = cancelled ? "已停止" : "异常",
                OverallStatusState = cancelled ? QuestVoiceStageState.Pending : QuestVoiceStageState.Failed,
                DialogueState = "未运行",
                StageSummary = cancelled ? "当前阶段：已停止" : "当前阶段：启动未完成",
                Error = cancelled ? "" : exception.Message
            });
            return false;
        }
    }

    /// <summary>立即拒绝新帧、取消启动与试听，并请求控制器停止；返回需要等待的收尾任务。</summary>
    private Task BeginStop(bool applicationExit)
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
        return Task.WhenAll(_controller.StopAsync(abortBackend: applicationExit), encoding);
    }

    /// <summary>在持有 _operations 时等待排空并更新状态；已发出的 HTTP 保留串行排空策略。</summary>
    private async Task FinishStopAsync(Task stopping)
    {
        try
        {
            Update(s => s with
            {
                IsRunning = false,
                IsBusy = true,
                Session = "停止中；等待已发出的后端请求收尾",
                StageSummary = "当前阶段：正在停止并排空已发出的请求"
            });
            await stopping.ConfigureAwait(false);
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
        }
    }

    /// <summary>未运行时说明原因，让卡片区分“已关闭”“等待截图器”“任务中暂停”和“设置中暂停”。</summary>
    private void UpdateIdleStatus()
    {
        if (_controller.IsRunning) return;
        var reason = !_config.QuestVoiceConfig.Enabled ? "已关闭"
            : _runtime.Current is not { IsStopping: false } ? "等待启动截图器"
            : _dispatcher.IsInTask ? "独立任务运行中，已暂停"
            : Volatile.Read(ref _settingsEditors) > 0 ? "设置窗口打开中，已暂停"
            : null;
        // 启动失败等情况保留已有的错误说明。
        if (reason is not null) Update(s => s.IsBusy ? s : s with { Session = reason });
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
            // 与实时触发器一致：暂停拾取（暂停热键、战斗、传送、选 F 选项）期间不处理帧，会话保留。
            if (RunnerContext.Instance.AutoPickTriggerStopCount > 0 || !_encoding.IsCompleted || _controller.IsObserving
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
                        var exitStart = Stopwatch.GetTimestamp();
                        var exit = DialogueExitDetector.HasConfirmedExit(copy);
                        metrics.Record(QuestVoiceMetric.ExitEvidence, Stopwatch.GetTimestamp() - exitStart);
                        // 编码结果直接移交给控制器，不再额外复制。
                        if (_controller.TryObserve(bytes, at, exit)) Update(s => s with { Capture = $"已采样 {copy.Width}×{copy.Height} · {_sampleInterval} ms" });
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

    /// <summary>静态资源和路由校验运行在工作线程，不阻塞窗口。</summary>
    public async Task ValidateAsync(CancellationToken token = default)
    {
        var result = await Task.Run(() => _factory.ValidateAsync(token), token).ConfigureAwait(false);
        Update(s => s with { Validation = result.Detail, Routing = result.RoutingSummary });
    }

    /// <summary>用户在手动恢复后明确确认；不把探测成功伪装成远端已空闲。确认后按开关重新启动。</summary>
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
        RequestApply();
    }

    /// <summary>只响应开关变化，其他设置在下次启动时生效。</summary>
    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuestVoiceConfig.Enabled)) RequestApply();
    }

    /// <summary>独立任务或脚本开始时停止，结束后按开关恢复。</summary>
    private void OnTaskModeChanged(object? sender, bool inTask) => RequestApply();

    /// <summary>绑定状态来自宿主，不猜测游戏进程。</summary>
    private void RefreshGame() => Update(s => s with { Game = _runtime.Current is { } runtime && !runtime.IsStopping
        ? $"已绑定 · PID {runtime.Window.ProcessId}" : "未绑定游戏；请先在启动页启用截图" });

    /// <summary>截图器启动后按开关自动开始。</summary>
    private void OnRuntimeStarted(object? sender, EventArgs e)
    {
        RefreshGame();
        RequestApply();
    }

    /// <summary>截图器停止或窗口失效时自动排空，后台故障必须被观察。</summary>
    private void OnRuntimeStopped(object? sender, EventArgs e)
    {
        if (_controller.IsRunning) _overlay.Disconnected();
        RefreshGame();
        RequestApply();
    }

    /// <summary>App.OnExit 已异步排空；此处只移除进程级事件。</summary>
    public void Dispose()
    {
        _dispatcher.FrameCaptured -= OnCaptured;
        _dispatcher.TaskModeChanged -= OnTaskModeChanged;
        _config.QuestVoiceConfig.PropertyChanged -= OnConfigChanged;
        _runtime.Started -= OnRuntimeStarted;
        _runtime.Stopped -= OnRuntimeStopped;
        _controller.FrameEvent -= OnFrameEvent;
        _controller.VoiceEvent -= OnVoiceEvent;
        _controller.ObservationFailed -= OnObservationFailed;
    }

    /// <summary>设置窗口的暂停租约；释放后恢复，重复释放无副作用。</summary>
    private sealed class SettingsEditLease(QuestVoiceService owner) : IDisposable
    {
        /// <summary>是否已经释放。</summary>
        private int _disposed;

        /// <summary>归还暂停计数并按期望状态恢复。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Interlocked.Decrement(ref owner._settingsEditors);
            owner.RequestApply();
        }
    }
}
