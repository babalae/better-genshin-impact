// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;

/// <summary>ContinuousFrameSession：原神自动配音的业务类型。</summary>
public sealed partial class ContinuousFrameSession
{
    /// <summary>关闭输入后自然排空最新有效候选与播放。</summary>
    public Task CompleteAsync(CancellationToken token = default)
    {
        TaskCompletionSource completion;
        lock (_ownership)
        {
            if (!_started) throw new InvalidOperationException("Start session first.");
            if (_stop is not null) return _stop.Task.WaitAsync(token);
            if (_complete is not null) return _complete.Task.WaitAsync(token);
            _accepting = false; completion = _complete = NewSignal();
        }
        _ = CompleteOwnedAsync(completion);
        return completion.Task.WaitAsync(token);
    }
    /// <summary>CompleteOwnedAsync：执行ContinuousFrameSession.Lifetime中的对应校验或计算步骤。</summary>
    private async Task CompleteOwnedAsync(TaskCompletionSource completion)
    {
        try
        {
            Task? start; lock (_ownership) start = _start?.Task;
            if (start is not null) await start.ConfigureAwait(false);
            Task observation; lock (_ownership) observation = _observation;
            try { await observation.ConfigureAwait(false); } catch (Exception) { /* Source error already reported. */ }
            await _publication.WaitAsync().ConfigureAwait(false);
            try { _inputEnded = true; Emit(ContinuousFrameEventKind.Completing); Pulse(); }
            finally { _publication.Release(); }
            await _ocrTask.ConfigureAwait(false);
            if (_voice is not null) await _voice.CompleteAsync().ConfigureAwait(false);
            UpdateSnapshot(true);
            Emit(ContinuousFrameEventKind.Completed, detail: _gate.Stable ? "Drained" : "FinalFrameUnstable");
            completion.TrySetResult();
        }
        catch (Exception exception) { completion.TrySetException(exception); }
    }
    /// <summary>拒绝后续工作、取消未播内容，并等待已拥有的任务排空。</summary>
    public Task StopAsync(CancellationToken token = default)
    {
        TaskCompletionSource completion;
        lock (_ownership)
        {
            if (_stop is not null) return _stop.Task.WaitAsync(token);
            _stopped = true; _accepting = false; completion = _stop = NewSignal();
        }
        // Never wait for inspection, OCR, or the publication semaphore before requesting voice stop.
        Task voiceStop;
        try { voiceStop = _voice?.StopAsync() ?? Task.CompletedTask; }
        catch (Exception exception) { voiceStop = Task.FromException(exception); }
        CancelSafely(_lifetime);
        Pulse();
        _ = StopOwnedAsync(completion, voiceStop);
        return completion.Task.WaitAsync(token);
    }
    /// <summary>StopOwnedAsync：执行ContinuousFrameSession.Lifetime中的对应校验或计算步骤。</summary>
    private async Task StopOwnedAsync(TaskCompletionSource completion, Task voiceStop)
    {
        try
        {
            Task? starting; lock (_ownership) starting = _startFinished?.Task;
            if (starting is not null) await starting.ConfigureAwait(false);
            Task observation; lock (_ownership) observation = _observation;
            try { await observation.ConfigureAwait(false); } catch (Exception) { }
            await _publication.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_pending is { } pending) EmitWork(ContinuousFrameEventKind.Discarded, pending, "SessionStopped");
                _pending = null; Emit(ContinuousFrameEventKind.Stopping); Pulse();
            }
            finally { _publication.Release(); }
            await Task.WhenAll(_ocrTask, voiceStop).ConfigureAwait(false);
            UpdateSnapshot(true); completion.TrySetResult();
        }
        catch (Exception exception) { completion.TrySetException(exception); }
    }
    /// <summary>异步排空拥有的任务，再释放本轮资源。</summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_ownership)
        {
            if (_dispose is not null) return new(_dispose.Task);
            completion = _dispose = NewSignal();
        }
        _ = DisposeOwnedAsync(completion);
        return new(completion.Task);
    }
    /// <summary>DisposeOwnedAsync：执行ContinuousFrameSession.Lifetime中的对应校验或计算步骤。</summary>
    private async Task DisposeOwnedAsync(TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception) { failure = exception; }
        try
        {
            Task? complete; lock (_ownership) complete = _complete?.Task;
            if (complete is not null) await complete.ConfigureAwait(false);
        }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        finally { _lifetime.Dispose(); _wake.Dispose(); _publication.Dispose(); }
        if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
    }
}
