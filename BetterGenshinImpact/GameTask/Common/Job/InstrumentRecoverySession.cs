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

    internal void RecordKeyDown(TimeSpan time) => _sentAt = time;
    internal bool IsAfterInput(TimeSpan capturedAt) => capturedAt > _sentAt;

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
