// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;

/// <summary>ContinuousVoiceSession：原神自动配音的业务类型。</summary>
public sealed partial class ContinuousVoiceSession
{
    /// <summary>新句完整音频或流式有效首段准备好且观察租约归还后，再接替旧句。</summary>
    private async Task PlaybackPumpAsync()
    {
        while (true)
        {
            Task wake;
            lock (_gate)
            {
                wake = _changed.Task;
                if (!_accepting && _synthesisDone && _ready is null && _playing is null) return;
                if (_ready is { } ready && _observations == 0 && IsCurrent(ready.Work.Candidate))
                {
                    if (_playing is { } playing)
                    {
                        RequestStop(playing, "Replacement audio ready");
                        wake = playing.Completion.Task;
                    }
                    else
                    {
                        _ready = null;
                        StartPlayback(ready);
                        // Re-evaluate after synchronous completion or subscriber reentry.
                        continue;
                    }
                }
            }
            await wake.ConfigureAwait(false);
        }
    }

    /// <summary>启动一个已经校验并保存的音频，记录实际播放所有权。</summary>
    private void StartPlayback(ReadyAudio ready)
    {
        if (!IsCurrent(ready.Work.Candidate) || _observations != 0) { ready.Stream?.Dispose(); return; }
        var playback = new Playback(ready.Work, ready.Key, ready.Stream);
        _playing = playback;
        Task operation;
        try
        {
            operation = ready.Stream is { } source
                ? ((IStreamingAudioPlayer)player).PlayStreamingAsync(source, playback.Cancellation.Token)
                : player.PlayAsync(ready.Bytes, playback.Cancellation.Token);
        }
        catch (Exception exception) { operation = Task.FromException(exception); }
        if (_currentKey == ready.Key) _currentPlayed = true;
        Emit(ContinuousVoiceEventKind.PlaybackStarted, ready.Work);
        _ = ObservePlaybackAsync(operation, playback);
    }

    /// <summary>观察播放的真实结束，释放取消源并允许下一句接替。</summary>
    private async Task ObservePlaybackAsync(Task operation, Playback playback)
    {
        Exception? failure = null;
        try { await operation.ConfigureAwait(false); }
        catch (OperationCanceledException) when (playback.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) { failure = exception; }
        finally { playback.Stream?.Dispose(); }
        lock (_gate)
        {
            var succeeded = failure is null && !playback.Cancellation.IsCancellationRequested;
            if (failure is not null)
            {
                _error = (failure as VoiceException)?.Code ?? VoiceErrorCode.PlaybackDevice;
                _errorDetail = failure.Message;
                if (_currentKey == playback.Key) _currentPlayed = false;
            }
            _playing = null;
            playback.Cancellation.Dispose();
            Emit(ContinuousVoiceEventKind.PlaybackCompleted, playback.Work,
                failure?.Message ?? (succeeded ? "Natural end" : "Stopped"), failure is null ? null : _error, succeeded);
            playback.Completion.TrySetResult();
            Pulse();
        }
    }

    /// <summary>只请求当前播放停止，完成状态由实际播放任务决定。</summary>
    private void RequestStop(Playback? playback, string reason)
    {
        if (playback is null || playback.StopRequested) return;
        playback.StopRequested = true;
        Emit(ContinuousVoiceEventKind.StopRequested, playback.Work, reason);
        CancelSafely(playback.Cancellation);
        try { player.Stop(); }
        catch (Exception exception)
        {
            _error = (exception as VoiceException)?.Code ?? VoiceErrorCode.PlaybackDevice;
            _errorDetail = exception.Message;
            Emit(ContinuousVoiceEventKind.PlaybackStopFailed, playback.Work, exception.Message, _error);
        }
    }
}
