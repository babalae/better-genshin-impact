// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using Serilog;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
/// <summary>ContinuousFrameSession：原神自动配音的业务类型。</summary>
public sealed partial class ContinuousFrameSession : IAsyncDisposable
{
    /// <summary>保护异步操作所有权，确保停止等待已发出的工作。</summary>
    private readonly object _ownership = new();
    /// <summary>保护通知队列，订阅者异常不会终止管线。</summary>
    private readonly object _notificationsGate = new();
    /// <summary>按顺序投递的事件队列。</summary>
    private readonly Queue<ContinuousFrameEvent> _notifications = new();
    /// <summary>保存 _publication, _wake 对应的本轮状态或配置，不跨会话复用。</summary>
    private readonly SemaphoreSlim _publication = new(1, 1), _wake = new(0, 1);
    /// <summary>本轮生命周期取消源。</summary>
    private readonly CancellationTokenSource _lifetime = new();
    /// <summary>本轮单调时钟，不受系统时间调整影响。</summary>
    private readonly Stopwatch _clock = new();
    /// <summary>保护会话状态的同步锁。</summary>
    private readonly DialogueFrameGate _gate;
    /// <summary>画面布局和墨迹签名观察器。</summary>
    private readonly IContinuousFrameInspector _inspector;
    /// <summary>稳定帧的 OCR 识别边界。</summary>
    private readonly IContinuousFrameRecognizer _recognizer;
    /// <summary>同轮配音协调器，由所有者负责排空。</summary>
    private readonly IContinuousVoiceSession? _voice;
    /// <summary>业务诊断日志入口。</summary>
    private readonly ILogger _logger;
    /// <summary>可选本轮计数器；不在热路径生成日志字符串。</summary>
    private readonly QuestVoiceMetrics? _metrics;
    /// <summary>供读取方使用的不可变状态快照。</summary>
    private ContinuousFrameState _snapshot = new(0, 0, false, false, false);
    /// <summary>保存 _observation, _ocrTask 对应的本轮状态或配置，不跨会话复用。</summary>
    private Task _observation = Task.CompletedTask, _ocrTask = Task.CompletedTask;
    /// <summary>保存 _start, _startFinished, _complete, _stop, _dispose 对应的本轮状态或配置，不跨会话复用。</summary>
    private TaskCompletionSource? _start, _startFinished, _complete, _stop, _dispose;
    /// <summary>保存 _started, _accepting, _observing, _inputEnded 对应的本轮状态或配置，不跨会话复用。</summary>
    private bool _started, _accepting, _observing, _inputEnded;
    /// <summary>标记正在投递事件，防止重入打乱事件顺序。</summary>
    private bool _notifying;
    /// <summary>停止后不再接受观察。</summary>
    private volatile bool _stopped;
    /// <summary>保存 _revision, _epoch, _eventSequence, _lastInputSequence 对应的本轮状态或配置，不跨会话复用。</summary>
    private long _revision, _epoch, _eventSequence, _lastInputSequence = -1;
    /// <summary>语音代际独立于视觉版本；只有接受新句、可信清空或离场才递增。</summary>
    private long _semanticGeneration, _dialogueOccurrence;
    /// <summary>等待后续独立OCR证据的小幅变化；只影响取证调度。</summary>
    private TextConfirmation? _confirmation;
    /// <summary>最近接受文字对应的可信对白锚点，用现有场景证据确认正文清空。</summary>
    private ContinuousFrameInspection? _dialogueAnchor;
    /// <summary>连续空正文的起点；单帧或跨观察间隙不构成话轮边界。</summary>
    private TimeSpan? _clearAt;
    /// <summary>连续可靠清空帧数量，以及本段清空是否已经提交。</summary>
    private int _clearFrames;
    /// <summary>清空证据仅提交一次，恢复文字后才允许下一话轮。</summary>
    private bool _clearCommitted;
    /// <summary>最近接受帧的单调观察时间。</summary>
    private TimeSpan? _lastInputTime;
    /// <summary>保存 _pending 对应的本轮状态或配置，不跨会话复用。</summary>
    private OcrWork? _pending;
    /// <summary>当前 OCR 工作取消源。</summary>
    private CancellationTokenSource? _activeOcr;
    /// <summary>最近正式接受的姓名与正文组合。</summary>
    private AcceptedText? _accepted;

    /// <summary>建立 ContinuousFrameSession 的业务依赖与初始状态。</summary>
    public ContinuousFrameSession(ContinuousSettings settings, IContinuousFrameInspector inspector,
        IContinuousFrameRecognizer recognizer, IContinuousVoiceSession? voice = null, ILogger? logger = null, QuestVoiceMetrics? metrics = null)
    { _gate = new(settings); _inspector = inspector; _recognizer = recognizer; _voice = voice; _logger = logger ?? Log.Logger; _metrics = metrics; }
    /// <summary>按发生顺序发布的业务事件。</summary>
    public event Action<ContinuousFrameEvent>? Event;
    /// <summary>读取当前不可变状态快照。</summary>
    public ContinuousFrameState Snapshot => Volatile.Read(ref _snapshot);
    /// <summary>启动当前单轮管线；重复调用沿用既有启动任务。</summary>
    public Task StartAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        TaskCompletionSource completion;
        lock (_ownership)
        {
            if (_stopped) throw new InvalidOperationException("Session is single-use.");
            if (_start is not null) return _start.Task.WaitAsync(token);
            completion = _start = NewSignal();
            _startFinished = NewSignal();
            _started = true; _clock.Start();
            _ocrTask = Task.Run(OcrPumpAsync);
        }
        _ = StartOwnedAsync(completion, token);
        return completion.Task;
    }
    /// <summary>StartOwnedAsync：执行ContinuousFrameSession中的对应校验或计算步骤。</summary>
    private async Task StartOwnedAsync(TaskCompletionSource completion, CancellationToken token)
    {
        try
        {
            if (_voice is not null) await _voice.StartAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lock (_ownership)
            {
                if (_stopped) throw new InvalidOperationException("Session stopped during start.");
                _accepting = _complete is null;
            }
            Emit(ContinuousFrameEventKind.Started);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            // Stop owns draining; awaiting it here would cycle through _startFinished.
            _ = StopAsync();
            completion.TrySetException(exception);
        }
        finally { _startFinished!.TrySetResult(); }
    }
    // A synchronous reservation and copy precede the first asynchronous boundary.
    public Task ObserveAsync(long sequence, TimeSpan observedAt, ReadOnlyMemory<byte> bytes, CancellationToken token = default, bool dialogueExitConfirmed = false)
    {
        TaskCompletionSource completion;
        lock (_ownership)
        {
            if (!_accepting || _stopped) return Task.FromException(new InvalidOperationException("Session no longer accepts frames."));
            if (_observing) return Task.FromException(new InvalidOperationException("Concurrent observations are not queued."));
            if (sequence < 0 || sequence <= _lastInputSequence || observedAt < TimeSpan.Zero || observedAt <= _lastInputTime)
                return Task.FromException(new ArgumentException("Frame sequence/time must strictly increase."));
            _lastInputSequence = sequence; _lastInputTime = observedAt; _observing = true;
            completion = NewSignal(); _observation = completion.Task;
        }
        IDisposable? lease = null;
        try
        {
            lease = _voice?.BeginObservation();
            var frame = new ContinuousFrameSnapshot(sequence, observedAt, bytes, dialogueExitConfirmed);
            _ = ObserveOwnedAsync(frame, lease, completion, token);
        }
        catch (Exception exception)
        {
            FinishObservation(lease, completion, exception);
        }
        return completion.Task;
    }
    /// <summary>处理已预留的观察工作并在结束时归还所有权。</summary>
    private async Task ObserveOwnedAsync(ContinuousFrameSnapshot frame, IDisposable? lease, TaskCompletionSource completion, CancellationToken token)
    {
        Exception? failure = null;
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var inspectionStart = Stopwatch.GetTimestamp();
            ContinuousFrameInspection inspection;
            try { inspection = await _inspector.InspectAsync(frame, cancellation.Token).ConfigureAwait(false); }
            finally { _metrics?.Record(QuestVoiceMetric.Inspection, Stopwatch.GetTimestamp() - inspectionStart); }
            await _publication.WaitAsync().ConfigureAwait(false);
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var decision = _gate.Observe(frame, inspection);
                Emit(ContinuousFrameEventKind.Observed, frame, decision.Reason, inspection);
                if (_stopped) throw new OperationCanceledException(_lifetime.Token);
                if (decision.Invalidate)
                {
                    Invalidate(frame, decision.Reason, decision.Leave);
                }
                ObserveDialogueClear(frame, inspection, decision);
                UpdateSnapshot();
                var targeted = ShouldConfirm(frame, decision);
                if ((decision.Recognize || targeted) && !_stopped)
                {
                    if (_pending is { } previous) EmitWork(ContinuousFrameEventKind.Discarded, previous, "LatestPendingReplaced");
                    _pending = new(frame, inspection, _revision, _epoch, Stopwatch.GetTimestamp());
                    if (targeted && _confirmation is { } confirmation)
                        _confirmation = confirmation with { Attempts = confirmation.Attempts + 1, LastRequestedAt = frame.ObservedAt };
                    EmitWork(ContinuousFrameEventKind.OcrQueued, _pending, targeted ? "TargetedTextConfirmation" : null); Pulse();
                }
            }
            finally { _publication.Release(); }
        }
        catch (Exception exception)
        {
            failure = exception;
            try
            {
                await _publication.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!_stopped) { _gate.Reset(); Invalidate(frame, "InspectionFailedOrCancelled"); }
                    Emit(ContinuousFrameEventKind.Failed, frame, exception.Message); UpdateSnapshot();
                }
                finally { _publication.Release(); }
            }
            catch (Exception reportingFailure)
            { failure = new AggregateException(exception, reportingFailure); }
        }
        finally { FinishObservation(lease, completion, failure); }
    }
    /// <summary>先归还租约和并发预留，再通知调用方完成。</summary>
    private void FinishObservation(IDisposable? lease, TaskCompletionSource completion, Exception? failure)
    {
        try { lease?.Dispose(); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        // A caller awaiting this task may submit the next frame immediately. Publish completion only
        // after both the voice publication barrier and our synchronous reservation have been released.
        lock (_ownership) _observing = false;
        if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
    }
    /// <summary>视觉版本只废弃截图/OCR；可信离场才结束语音所有权。</summary>
    private void Invalidate(ContinuousFrameSnapshot frame, string reason, bool leave = false)
    {
        _revision++;
        CancelSafely(_activeOcr);
        if (_pending is { } pending) EmitWork(ContinuousFrameEventKind.Discarded, pending, reason);
        _pending = null; _confirmation = null;
        _clearAt = null; _clearFrames = 0;
        if (leave)
        {
            _epoch++; _accepted = null; _dialogueAnchor = null; _clearCommitted = false;
            // The voice session owns this playback wait. Its pumps are drained by our terminal path.
            _semanticGeneration++;
            if (_voice is not null) _ = _voice.LeaveDialogueAsync(_semanticGeneration);
            Emit(ContinuousFrameEventKind.LeftDialogue, frame, reason);
        }
        Emit(ContinuousFrameEventKind.Invalidated, frame, reason);
    }
    /// <summary>请求取消并记录回调异常，仍然继续排空所有权。</summary>
    private void CancelSafely(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (AggregateException exception) { _logger.Error(exception, "Continuous frame cancellation callback failed; draining owned work"); }
        catch (ObjectDisposedException) { /* Completion already released the cancellation source. */ }
    }
    /// <summary>唤醒管线泵重新检查可执行工作。</summary>
    private void Pulse() { if (_wake.CurrentCount == 0) try { _wake.Release(); } catch (SemaphoreFullException) { } }
    /// <summary>发布供其他线程读取的不可变快照。</summary>
    private void UpdateSnapshot(bool completed = false) => Volatile.Write(ref _snapshot, new(_revision, _epoch, _gate.Stable, completed, _stopped, _semanticGeneration));
    /// <summary>创建异步执行延续的完成信号，避免在状态锁内运行调用方。</summary>
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>OcrWork：原神自动配音的业务类型。</summary>
    private sealed record OcrWork(ContinuousFrameSnapshot Frame, ContinuousFrameInspection Inspection, long Revision, long Epoch, long QueuedAt);
    /// <summary>有界取证状态；不通过定时器制造接受证据。</summary>
    private sealed record TextConfirmation(AcceptedText Text, long Revision, long Epoch, long Generation,
        long SourceSequence, TimeSpan LastRequestedAt, int Attempts = 0);
    /// <summary>AcceptedText：原神自动配音的业务类型。</summary>
    private sealed record AcceptedText(string? Name, Core.Routing.SpeakerNameStatus Status, string Text);
}
