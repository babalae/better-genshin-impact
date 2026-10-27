// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using Serilog;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;

/// <summary>Owns two bounded pumps, not its injected dependencies. The DI scope must await disposal
/// before releasing the engine/player/logger. Ordinary cancellation never shuts down the backend.</summary>
public sealed partial class ContinuousVoiceSession(AppSettings settings, ITtsEngine engine, IWavProcessor wav,
    IAudioArtifactStore artifacts, IAudioPlayer player, IVoiceRoutingService? routing = null,
    ILogger? logger = null) : IContinuousVoiceSession
{
    /// <summary>保护会话状态的同步锁。</summary>
    private readonly object _gate = new();
    /// <summary>本轮单调时钟，不受系统时间调整影响。</summary>
    private readonly Stopwatch _clock = new();
    /// <summary>全部拥有的工作完成信号。</summary>
    private readonly TaskCompletionSource _completion = NewSignal();
    /// <summary>唤醒等待中的管线泵。</summary>
    private TaskCompletionSource _changed = NewSignal();
    /// <summary>按顺序投递的事件队列。</summary>
    private readonly Queue<ContinuousVoiceEvent> _notifications = new();
    /// <summary>标记正在投递事件，防止重入打乱事件顺序。</summary>
    private bool _notifying;
    /// <summary>保存 _started, _accepting, _stopping, _completed, _synthesisDone, _backendUnknown 对应的本轮状态或配置，不跨会话复用。</summary>
    private bool _started, _accepting, _stopping, _completed, _synthesisDone, _backendUnknown;
    /// <summary>保存 _revision, _epoch, _sequence 对应的本轮状态或配置，不跨会话复用。</summary>
    private long _revision, _epoch, _sequence;
    /// <summary>已经提交候选的语义代际，覆盖待处理、合成、Ready与已播所有权。</summary>
    private long? _offeredRevision;
    /// <summary>尚未归还的画面观察租约数量，阻止播放旧版本。</summary>
    private int _observations;
    /// <summary>保存 _pending, _synthesizing 对应的本轮状态或配置，不跨会话复用。</summary>
    private ContinuousVoiceWork? _pending, _synthesizing;
    /// <summary>当前合成候选的取消源。</summary>
    private CancellationTokenSource? _synthesisCancellation;
    /// <summary>完整音频或已达到有效PCM水位、等待接替旧句的流。</summary>
    private ReadyAudio? _ready;
    /// <summary>当前播放及其完成信号。</summary>
    private Playback? _playing;
    /// <summary>当前语音的规范身份、正文与对白周期组合。</summary>
    private DialogueKey? _currentKey;
    /// <summary>当前身份是否已经开始播放，防止重复朗读。</summary>
    private bool _currentPlayed;
    /// <summary>最近一次业务错误代码。</summary>
    private VoiceErrorCode? _error;
    /// <summary>最近一次业务错误详情。</summary>
    private string? _errorDetail;

    /// <summary>按发生顺序发布的业务事件。</summary>
    public event Action<ContinuousVoiceEvent>? Event;
    /// <summary>读取当前不可变状态快照。</summary>
    public ContinuousVoiceSnapshot Snapshot
    {
        get
        {
            lock (_gate) return new(_started, _accepting, _stopping, _completed, _revision, _epoch,
                _pending, _synthesizing, _ready?.Work, _playing?.Work, _backendUnknown, _error, _errorDetail, _observations);
        }
    }

    /// <summary>启动当前单轮管线；重复调用沿用既有启动任务。</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_stopping || _completed) throw new InvalidOperationException("The session is single-use.");
            if (_started) return Task.CompletedTask;
            _started = _accepting = true; _clock.Start();
            // Lifetime ownership exists before Started can synchronously call Stop/Dispose.
            _ = FinishAsync(Task.Run(SynthesisPumpAsync), Task.Run(PlaybackPumpAsync));
            Emit(ContinuousVoiceEventKind.Started);
        }
        return Task.CompletedTask;
    }

    /// <summary>递增语义代际并丢弃尚未播放的旧内容。</summary>
    public void Invalidate(long revision)
    {
        lock (_gate)
        {
            RequireStarted();
            if (!_accepting) return;
            InvalidateCore(revision);
        }
    }

    /// <summary>在状态锁内发布新的语义代际。</summary>
    private void InvalidateCore(long revision)
    {
        if (revision <= _revision) throw new ArgumentOutOfRangeException(nameof(revision), "Revision must increase.");
        _revision = revision;
        var epoch = _epoch;
        DiscardUnplayed("New semantic generation");
        Emit(ContinuousVoiceEventKind.Invalidated, revision: revision, epoch: epoch);
        Pulse();
    }

    /// <summary>只接受当前版本与周期中尚未提交的对白候选。</summary>
    public void Offer(ContinuousDialogueCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            RequireStarted();
            if (!_accepting || !IsCurrent(candidate) || _backendUnknown || _offeredRevision == candidate.Generation)
            {
                Emit(ContinuousVoiceEventKind.Discarded, new(candidate), "Candidate is stale, repeated, or session unavailable");
                return;
            }
            if (candidate.SourceSequence < 0 || candidate.ObservedAt < TimeSpan.Zero || string.IsNullOrWhiteSpace(candidate.Dialogue))
                throw new ArgumentException("A candidate needs valid source metadata and dialogue.", nameof(candidate));
            _offeredRevision = candidate.Generation;
            _pending = new(candidate);
            Emit(ContinuousVoiceEventKind.CandidateOffered, _pending);
            Pulse();
        }
    }

    /// <summary>离开对白时切换周期，停止播放并等待其实际退出。</summary>
    public Task LeaveDialogueAsync(long revision)
    {
        lock (_gate)
        {
            RequireStarted();
            if (!_accepting) return _completion.Task;
            if (revision <= _revision) throw new ArgumentOutOfRangeException(nameof(revision), "Revision must increase.");
            _epoch++; _currentKey = null; _currentPlayed = false;
            var epoch = _epoch;
            InvalidateCore(revision);
            var playing = _playing;
            RequestStop(playing, "Left dialogue");
            Emit(ContinuousVoiceEventKind.LeftDialogue, revision: revision, epoch: epoch);
            return playing?.Completion.Task ?? Task.CompletedTask;
        }
    }

    /// <summary>取得观察租约，在画面版本判定之前阻止新播放。</summary>
    public IDisposable BeginObservation()
    {
        lock (_gate)
        {
            RequireStarted();
            if (!_accepting) throw new InvalidOperationException("Session no longer accepts observations.");
            _observations++;
            return new ObservationLease(this);
        }
    }

    /// <summary>归还观察租约并重新唤醒播放。</summary>
    private void EndObservation()
    {
        lock (_gate) { _observations--; Pulse(); }
    }

    /// <summary>关闭输入后自然排空最新有效候选与播放。</summary>
    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            RequireStarted();
            if (_accepting)
            {
                _accepting = false;
                Emit(ContinuousVoiceEventKind.Completing);
                Pulse();
            }
            return WaitForCompletion(cancellationToken);
        }
    }

    /// <summary>拒绝后续工作、取消未播内容，并等待已拥有的任务排空。</summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_stopping && !_completed)
            {
                _stopping = true; _accepting = false;
                DiscardUnplayed("Session stopped");
                RequestStop(_playing, "Session stopped");
                Emit(ContinuousVoiceEventKind.Stopping);
                if (!_started) { _completed = true; _completion.TrySetResult(); }
                Pulse();
            }
            return WaitForCompletion(cancellationToken);
        }
    }

    /// <summary>异步排空拥有的任务，再释放本轮资源。</summary>
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>可取消调用方等待，但不放弃实际任务所有权。</summary>
    private Task WaitForCompletion(CancellationToken token) => token.CanBeCanceled
        ? _completion.Task.WaitAsync(token) : _completion.Task;

    /// <summary>观察两个管线泵完成并发布终态。</summary>
    private async Task FinishAsync(Task synthesis, Task playback)
    {
        try
        {
            await Task.WhenAll(synthesis, playback).ConfigureAwait(false);
            lock (_gate) { _completed = true; Emit(ContinuousVoiceEventKind.Completed); }
            _completion.TrySetResult();
        }
        catch (Exception exception) { _completion.TrySetException(exception); }
    }

    /// <summary>丢弃待合成和已就绪但尚未播放的旧候选。</summary>
    private void DiscardUnplayed(string reason)
    {
        var pending = _pending; var ready = _ready;
        _pending = null; _ready = null;
        ready?.Stream?.Dispose();
        CancelSafely(_synthesisCancellation);
        if (pending is not null) Emit(ContinuousVoiceEventKind.Discarded, pending, reason);
        if (ready is not null) Emit(ContinuousVoiceEventKind.Discarded, ready.Work, reason);
    }

    /// <summary>判断候选是否仍属于当前语义代际和对白周期。</summary>
    private bool IsCurrent(ContinuousDialogueCandidate candidate) => !_stopping
        && candidate.Generation == _revision && candidate.Epoch == _epoch;
    /// <summary>同时检查取消、语义代际与对白周期。</summary>
    private void CheckCurrent(ContinuousVoiceWork work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsCurrent(work.Candidate)) throw new OperationCanceledException(token);
    }
    /// <summary>拒绝尚未启动的单轮会话操作。</summary>
    private void RequireStarted()
    {
        if (!_started) throw new InvalidOperationException("Start the session first.");
    }
    /// <summary>创建异步执行延续的完成信号，避免在状态锁内运行调用方。</summary>
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>唤醒管线泵重新检查可执行工作。</summary>
    private void Pulse() { var old = _changed; _changed = NewSignal(); old.TrySetResult(); }

    /// <summary>请求取消并记录回调异常，仍然继续排空所有权。</summary>
    private void CancelSafely(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (AggregateException exception)
        { (logger ?? Log.Logger).Error(exception, "Continuous voice cancellation callback failed; draining owned work"); }
        catch (ObjectDisposedException) { /* Reentrant playback completion already released the source. */ }
    }

    // Subscribers run under the reentrant monitor so revision/stop decisions are immediately visible.
    // Each pipeline boundary rechecks after notification. Await lifecycle tasks asynchronously; never
    // synchronously block a callback waiting for the operation that is publishing that callback.
    private void Emit(ContinuousVoiceEventKind kind, ContinuousVoiceWork? work = null, string? detail = null,
        VoiceErrorCode? error = null, bool playbackSucceeded = false, long? revision = null, long? epoch = null)
    {
        var value = new ContinuousVoiceEvent(++_sequence, _clock.Elapsed, kind, work?.Candidate.Generation ?? revision ?? _revision,
            work?.Candidate.Epoch ?? epoch ?? _epoch, work?.Candidate.SourceSequence, work, detail, error, playbackSucceeded, _backendUnknown);
        _notifications.Enqueue(value);
        if (_notifying) return;
        _notifying = true;
        try
        {
            while (_notifications.TryDequeue(out var next))
            {
                (logger ?? Log.Logger).Information("{Event}: {@ContinuousVoiceEvent}", "ContinuousVoice", next);
                if (Event is not { } handlers) continue;
                foreach (Action<ContinuousVoiceEvent> handler in handlers.GetInvocationList())
                {
                    try { handler(next); }
                    catch (Exception exception) { (logger ?? Log.Logger).Error(exception, "Continuous voice subscriber failed"); }
                }
            }
        }
        finally { _notifying = false; }
    }

    /// <summary>ObservationLease：原神自动配音的业务类型。</summary>
    private sealed class ObservationLease(ContinuousVoiceSession session) : IDisposable
    {
        /// <summary>当前持有的会话引用。</summary>
        private ContinuousVoiceSession? _session = session;
        /// <summary>释放本实例拥有的本地资源。</summary>
        public void Dispose() => Interlocked.Exchange(ref _session, null)?.EndObservation();
    }
    /// <summary>DialogueKey：原神自动配音的业务类型。</summary>
    private sealed record DialogueKey(long Epoch, long Occurrence, ContinuousVoiceIdentity Identity, string Text,
        string GptWeights, string SovitsWeights, string References, TtsProviderSettings? Provider, string? Emotion);
    /// <summary>ReadyAudio：原神自动配音的业务类型。</summary>
    private sealed record ReadyAudio(ContinuousVoiceWork Work, DialogueKey Key, byte[] Bytes, StreamingPcmAudio? Stream = null);
    /// <summary>Playback：原神自动配音的业务类型。</summary>
    private sealed class Playback(ContinuousVoiceWork work, DialogueKey key, StreamingPcmAudio? stream = null)
    {
        /// <summary>由Ready转交的播放引用，不与候选取消令牌绑定。</summary>
        public StreamingPcmAudio? Stream { get; } = stream;
        /// <summary>Work：当前业务对象的配置、身份或状态值。</summary>
        public ContinuousVoiceWork Work { get; } = work;
        /// <summary>Key：当前业务对象的配置、身份或状态值。</summary>
        public DialogueKey Key { get; } = key;
        /// <summary>Cancellation：当前业务对象的配置、身份或状态值。</summary>
        public CancellationTokenSource Cancellation { get; } = new();
        /// <summary>Completion：当前业务对象的配置、身份或状态值。</summary>
        public TaskCompletionSource Completion { get; } = NewSignal();
        /// <summary>StopRequested：当前业务对象的配置、身份或状态值。</summary>
        public bool StopRequested { get; set; }
    }
}
