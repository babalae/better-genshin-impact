using System;
using BetterGenshinImpact.GameTask.Music.Service;

namespace BetterGenshinImpact.GameTask.Common.Job;

// 一次恢复只持有原步骤的演奏进度和大世界确认状态。
internal sealed class InstrumentRecoverySession(bool playDurin)
{
    internal DurinPlayback? Playback { get; } = playDurin ? new DurinPlayback() : null;
    internal bool CanPlay => Playback is { Completed: false, Stopped: false };
    private bool _sawWorld;
    private TimeSpan _sentAt;
    private TimeSpan _lastCapture;
    private (int Sheet, int Slot, char Key)? _proposal;

    internal void RecordKeyDown(TimeSpan time) => _sentAt = time;
    internal bool IsAfterInput(TimeSpan capturedAt) => capturedAt > _sentAt;
    internal bool CanObserveCapture(TimeSpan capturedAt) => IsAfterInput(capturedAt) && capturedAt > _lastCapture;

    // 连续两张新画面必须独立读出同一谱位和唯一光圈。缓存和发键前的旧帧不参与判定。
    internal bool ObserveCapturedFrame(TimeSpan capturedAt, InstrumentExitScreen screen, bool sameFrame,
        ScoreReading reading, uint rings, Action<char> send, Func<char, bool> verify)
    {
        if (!CanObserveCapture(capturedAt)) return false;
        _lastCapture = capturedAt;
        if (sameFrame) return Observe(screen, true, reading, rings, send, verify);

        var previous = _proposal;
        _proposal = null;
        return Observe(screen, false, reading, rings, send, key =>
        {
            var current = (Playback!.Sheet, Playback.NextNote, key);
            if (previous == current && verify(key)) return true;
            _proposal = current;
            return false;
        });
    }

    internal bool Observe(InstrumentExitScreen screen, bool sameFrame, ScoreReading reading, uint rings,
        Action<char> send, Func<char, bool>? verify = null)
    {
        if (screen == InstrumentExitScreen.World)
        {
            Playback?.MissingPage();
            if (_sawWorld && !sameFrame) return true;
            _sawWorld = true;
            return false;
        }
        _sawWorld = false;
        if (screen != InstrumentExitScreen.InstrumentPage) _proposal = null;
        if (screen is (InstrumentExitScreen.BlockedPrompt or InstrumentExitScreen.ExitPrompt)
            && Playback is { BetweenSheets: false })
            Playback?.Stop();
        else if (screen != InstrumentExitScreen.InstrumentPage)
            Playback?.MissingPage();
        else if (!sameFrame && CanPlay)
            Playback!.Observe(reading, rings, send, verify);
        return false;
    }
}
