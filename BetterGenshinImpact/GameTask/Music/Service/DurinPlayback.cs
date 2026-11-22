using System;

namespace BetterGenshinImpact.GameTask.Music.Service;

/// <summary>只推进已经识别并确认命中的谱位。调用方每张不同的演奏画面调用一次。</summary>
internal sealed class DurinPlayback
{
    internal const string KeyLayout = "QWERTYUASDFGHJZXCVBNM";
    private static readonly string[] Scores = ["HDHGFDFD", "YTRERTEJH", "DHJQWEWQJQ"];
    private const int ConfirmationFrames = 45;
    private int _neutralFrames;
    private int _framesSinceSend;
    private uint _previousHits;
    private bool _armed;
    private bool _pending;
    private bool _awaitingRestart;
    private bool _sawFirstSlotRed;
    private bool _retried;

    internal int Sheet { get; private set; }
    internal int NextNote { get; private set; }
    internal bool Completed => Sheet == Scores.Length;
    internal bool Stopped { get; private set; }
    internal bool BetweenSheets => Sheet > 0 && !_armed && !_awaitingRestart && !Completed;
    internal void Stop()
    {
        if (!Completed) Stopped = true;
    }

    internal void MissingPage()
    {
        if (_armed || _pending || _awaitingRestart)
            Stopped = true;
        _neutralFrames = 0;
    }

    internal void Observe(ScoreReading reading, uint rings, Action<char> send, Func<char, bool>? verify = null)
    {
        if (Stopped || Completed) return;

        if (reading.Red != 0)
        {
            if (_armed || _awaitingRestart)
            {
                if (_retried) Stopped = true;
                _awaitingRestart = true;
                _sawFirstSlotRed |= (reading.Red & 1) != 0;
                _pending = _armed = false;
            }
            _neutralFrames = 0;
            _previousHits = 0;
            return;
        }

        if (!_armed)
        {
            if (!reading.FullScore || reading.Hits != 0 || reading.PressedKeys != 0)
            {
                _neutralFrames = 0;
                return;
            }
            _neutralFrames = Math.Min(2, _neutralFrames + 1);
            if (_neutralFrames < 2) return;
            if (_awaitingRestart)
            {
                if (!_sawFirstSlotRed || rings != KeyBit(Scores[Sheet][0])) return;
                _awaitingRestart = false;
                _retried = true;
                NextNote = 0;
            }
            _armed = true;
        }

        uint slot = 1u << NextNote;
        char key = Scores[Sheet][NextNote];
        uint freshHits = reading.Hits & ~_previousHits & ~(slot - 1);
        _previousHits = reading.Hits;
        if (_pending)
        {
            _framesSinceSend++;
            if (freshHits == slot && reading.RemainingScore && reading.PressedKeys == KeyBit(key))
            {
                _pending = false;
                if (++NextNote == Scores[Sheet].Length)
                {
                    Sheet++;
                    NextNote = 0;
                    _armed = _retried = _sawFirstSlotRed = false;
                    _neutralFrames = 0;
                    _previousHits = 0;
                }
            }
            else if (freshHits != 0 || _framesSinceSend >= ConfirmationFrames)
            {
                Stopped = true;
            }
            return;
        }

        if (freshHits != 0)
        {
            Stopped = true;
            return;
        }
        if (!reading.RemainingScore || rings != KeyBit(key) || reading.PressedKeys != 0) return;

        try
        {
            if (verify != null && !verify(key)) return;
            send(key);
            // 只有实际输入调用成功返回后，后续画面才占用确认帧数。
            _pending = true;
            _framesSinceSend = 0;
        }
        catch
        {
            Stopped = true;
            throw;
        }
    }

    private static uint KeyBit(char key) => 1u << KeyLayout.IndexOf(key);
}
