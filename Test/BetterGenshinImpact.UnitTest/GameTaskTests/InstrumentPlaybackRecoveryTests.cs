using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Music.Service;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class InstrumentPlaybackRecoveryTests
{
    private static readonly ScoreReading Neutral = new(true, true, 0, 0);
    private const string Layout = "QWERTYUASDFGHJZXCVBNM";
    private static uint Key(char key) => 1u << Layout.IndexOf(key);

    [Fact]
    public void SimulatedInputCompletesThreeScoresAndWaitsForDistinctWorldFrames()
    {
        var recovery = new InstrumentRecoverySession(true);
        string[] scores = ["HDHGFDFD", "YTRERTEJH", "DHJQWEWQJQ"];
        var sent = new List<char>();
        char? received = null;
        void Receive(char key)
        {
            Assert.Null(received);
            sent.Add(key);
            received = key;
        }

        foreach (var score in scores)
        {
            // 演奏前和两张谱之间的剧情由玩家推进。
            for (var i = 0; i < 90; i++)
                Assert.False(recovery.Observe(InstrumentExitScreen.Other, false, default, 0, Receive));
            if (recovery.Playback!.BetweenSheets)
                Assert.False(recovery.Observe(InstrumentExitScreen.BlockedPrompt, false, default, 0, Receive));
            for (var i = 0; i < 90; i++)
                Assert.False(recovery.Observe(InstrumentExitScreen.InstrumentPage, false, Neutral, 0, Receive));
            for (var slot = 0; slot < score.Length; slot++)
            {
                char expected = score[slot];
                int count = sent.Count;
                recovery.Observe(InstrumentExitScreen.InstrumentPage, false, Neutral, Key(expected), Receive);
                Assert.Equal(expected, received);
                // 缓存重复帧和上一音的特效消失均不可推动或消耗当前音的确认进度。
                for (var i = 0; i < 80; i++)
                    recovery.Observe(InstrumentExitScreen.InstrumentPage, true, Neutral, Key(expected), Receive);
                for (var i = 0; i < 44; i++)
                    recovery.Observe(InstrumentExitScreen.InstrumentPage, false, Neutral, Key(expected), Receive);
                Assert.Equal(slot, recovery.Playback!.NextNote);
                Assert.Equal(count + 1, sent.Count);

                // 模拟后端只对它实际收到的键返回反馈，第 45 张新画面仍可确认。
                var feedback = Neutral with { Hits = 1u << slot, PressedKeys = Key(received!.Value) };
                recovery.Observe(InstrumentExitScreen.InstrumentPage, false, feedback, Key(expected), Receive);
                received = null;
                Assert.Equal(count + 1, sent.Count);
                Assert.Equal((slot + 1) % score.Length, recovery.Playback.NextNote);
            }
        }
        Assert.True(recovery.Playback!.Completed);
        recovery.Observe(InstrumentExitScreen.BlockedPrompt, false, default, 0, Receive);
        Assert.False(recovery.Playback.Stopped);
        for (var i = 0; i < 120; i++)
            Assert.False(recovery.Observe(InstrumentExitScreen.Other, false, default, 0, Receive));
        Assert.False(recovery.Observe(InstrumentExitScreen.World, false, default, 0, Receive));
        Assert.False(recovery.Observe(InstrumentExitScreen.World, true, default, 0, Receive));
        Assert.False(recovery.Observe(InstrumentExitScreen.Other, false, default, 0, Receive));
        Assert.False(recovery.Observe(InstrumentExitScreen.World, false, default, 0, Receive));
        Assert.True(recovery.Observe(InstrumentExitScreen.World, false, default, 0, Receive));
        Assert.Equal(string.Concat(scores), new string(sent.ToArray()));
    }

    [Fact]
    public void RedStopsInputUntilFirstSlotResetAndFreshMatchingCue()
    {
        var recovery = new InstrumentRecoverySession(true);
        var sent = new List<char>();
        void Tick(ScoreReading frame, uint cue = 0) =>
            recovery.Observe(InstrumentExitScreen.InstrumentPage, false, frame, cue, sent.Add);
        Tick(Neutral);
        Tick(Neutral, Key('H'));
        Tick(Neutral with { Hits = 1, PressedKeys = Key('H') });
        Tick(Neutral, Key('D'));
        Tick(Neutral with { Red = 2 }, Key('H'));
        for (int i = 0; i < 50; i++) Tick(Neutral, Key('H'));
        Assert.Equal("HD", new string(sent.ToArray()));
        Tick(Neutral with { Red = 1 });
        Tick(Neutral);
        Tick(Neutral, Key('H') | Key('D'));
        Assert.Equal(2, sent.Count);
        Tick(Neutral, Key('H'));
        Assert.Equal("HDH", new string(sent.ToArray()));
        Tick(Neutral with { Red = 1 });
        Tick(Neutral);
        Tick(Neutral, Key('H'));
        Assert.True(recovery.Playback!.Stopped);
        Assert.Equal(3, sent.Count);
    }

    [Fact]
    public void NewCaptureMustStillAuthorizeInputAndOnlyLaterCapturesCanAcknowledgeIt()
    {
        var recovery = new InstrumentRecoverySession(true);
        var sent = new List<char>();
        var beforeSend = TimeSpan.FromMilliseconds(24);
        var dispatched = TimeSpan.FromMilliseconds(37);
        void Send(char key)
        {
            sent.Add(key);
            recovery.RecordKeyDown(dispatched);
        }
        recovery.Observe(InstrumentExitScreen.InstrumentPage, false, Neutral, 0, Send);
        for (int i = 0; i < 90; i++)
            recovery.Observe(InstrumentExitScreen.InstrumentPage, false, Neutral, Key('H'), Send, _ => false);
        Assert.Empty(sent);
        recovery.Observe(InstrumentExitScreen.InstrumentPage, false, Neutral, Key('H'), Send, _ => true);
        Assert.Equal(new[] { 'H' }, sent);
        Assert.False(recovery.IsAfterInput(beforeSend));
        Assert.False(recovery.IsAfterInput(dispatched));
        Assert.True(recovery.IsAfterInput(dispatched + TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void MissingAcknowledgementStopsWithoutResendingEvenIfALateHitAppears()
    {
        var playback = new DurinPlayback();
        var sent = new List<char>();
        playback.Observe(Neutral, 0, sent.Add);
        playback.Observe(Neutral, Key('H'), sent.Add);
        for (int fresh = 1; fresh < 45; fresh++)
        {
            playback.Observe(Neutral, Key('H'), sent.Add);
            Assert.False(playback.Stopped);
        }
        playback.Observe(Neutral, Key('H'), sent.Add);
        Assert.True(playback.Stopped);
        playback.Observe(Neutral with { Hits = 1, PressedKeys = Key('H') }, Key('D'), sent.Add);
        Assert.Equal(0, playback.NextNote);
        Assert.Equal(new[] { 'H' }, sent);
    }

    [Fact]
    public void FailedDispatchCannotStartAnotherAttempt()
    {
        var playback = new DurinPlayback();
        var sent = new List<char>();
        playback.Observe(Neutral, 0, sent.Add);
        Assert.Throws<InvalidOperationException>(() =>
            playback.Observe(Neutral, Key('H'), _ => throw new InvalidOperationException()));
        playback.Observe(Neutral, Key('H'), sent.Add);
        Assert.True(playback.Stopped);
        Assert.Equal(0, playback.NextNote);
        Assert.Empty(sent);
    }

    [Fact]
    public void UnknownDialogOrUndeclaredQuestCannotSendNotes()
    {
        foreach (bool required in new[] { true, false })
        {
            var recovery = new InstrumentRecoverySession(required);
            var sent = new List<char>();
            if (required)
                recovery.Observe(InstrumentExitScreen.BlockedPrompt, false, Neutral, Key('H'), sent.Add);
            for (int i = 0; i < 50; i++)
                recovery.Observe(InstrumentExitScreen.InstrumentPage, false, Neutral, Key('H'), sent.Add);
            Assert.Empty(sent);
        }
    }
}
