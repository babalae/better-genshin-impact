using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousVoiceSessionTests。</summary>
public sealed class ContinuousVoiceSessionTests
{
    /// <summary>移植回归验证：Replacement waits for routing synthesis save and actual old playback exit。</summary>
    [Fact]
    public async Task Replacement_waits_for_routing_synthesis_save_and_actual_old_playback_exit()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A");
        await f.Routing.NextAsync(); await f.Artifacts.NextAsync();
        f.Routing.Hold = true; f.Artifacts.Hold = true;
        f.Offer(2, "B");
        var route = await f.Routing.NextAsync();
        Assert.False(a.Token.IsCancellationRequested); Assert.Equal(0, f.Player.StopCount);
        route.Release.SetResult(true);
        var b = await f.Engine.NextAsync();
        Assert.Equal(0, f.Player.StopCount); b.Complete();
        var save = await f.Artifacts.NextAsync();
        Assert.Equal(0, f.Player.StopCount); save.Complete();
        await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(a.Token.IsCancellationRequested); Assert.Single(f.Player.All);
        Assert.DoesNotContain(f.Events, e => e.Kind == ContinuousVoiceEventKind.PlaybackCompleted);
        a.Exit.SetResult(true);
        var next = await f.Player.NextAsync(); Assert.Equal("B", next.Text);
        next.Exit.SetResult(true); await f.Session.CompleteAsync();
        Assert.Equal(1, f.Player.MaximumActive);
        Assert.Equal(new[] { "A", "B" }, f.Player.All.Select(p => p.Text));
    }

    /// <summary>移植回归验证：Latest pending is bounded and late synthesis cannot replace A。</summary>
    [Fact]
    public async Task Latest_pending_is_bounded_and_late_synthesis_cannot_replace_A()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); f.Offer(2, "B"); var b = await f.Engine.NextAsync();
        for (var revision = 3; revision <= 100; revision++) f.Offer(revision, "C" + revision);
        Assert.Equal(2, f.Engine.All.Count); Assert.Equal(100, f.Session.Snapshot.Pending!.Candidate.Revision);
        Assert.True(b.Token.IsCancellationRequested); Assert.False(a.Token.IsCancellationRequested);
        b.Complete(); var c = await f.Engine.NextAsync(); Assert.Equal("C100", c.Request.Text);
        c.Complete(); await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10)); a.Exit.SetResult(true);
        var next = await f.Player.NextAsync(); Assert.Equal("C100", next.Text);
    }

    /// <summary>移植回归验证：Failure leaves A playing and new revision can retry without retry loop。</summary>
    [Fact]
    public async Task Failure_leaves_A_playing_and_new_revision_can_retry_without_retry_loop()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); f.Offer(2, "B"); var b = await f.Engine.NextAsync();
        b.Result.SetException(new VoiceException(VoiceErrorCode.SynthesisRejected, "rejected"));
        await f.EventAsync(ContinuousVoiceEventKind.CandidateFailed, 2);
        Assert.Equal(0, f.Player.StopCount); Assert.Equal(2, f.Engine.All.Count);
        f.Offer(3, "B"); var retry = await f.Engine.NextAsync(); Assert.Equal("B", retry.Request.Text);
        retry.Complete(); await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>移植回归验证：Observation lease defers replacement and invalidation discards ready audio。</summary>
    [Fact]
    public async Task Observation_lease_defers_replacement_and_invalidation_discards_ready_audio()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A");
        using (f.Session.BeginObservation())
        {
            f.Offer(2, "B"); (await f.Engine.NextAsync()).Complete();
            await f.EventAsync(ContinuousVoiceEventKind.AudioReady, 2);
            Assert.Equal(0, f.Player.StopCount); f.Session.Invalidate(3);
        }
        Assert.False(a.Token.IsCancellationRequested); Assert.Single(f.Player.All);
        a.Exit.SetResult(true); await f.Session.CompleteAsync();
    }

    /// <summary>移植回归验证：Natural end is silence until replacement and complete waits for final playback。</summary>
    [Fact]
    public async Task Natural_end_is_silence_until_replacement_and_complete_waits_for_final_playback()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); a.Exit.SetResult(true);
        await f.EventAsync(ContinuousVoiceEventKind.PlaybackCompleted, 1);
        f.Offer(2, "B"); var b = await f.Engine.NextAsync();
        Assert.Null(f.Session.Snapshot.Playing); Assert.Equal(0, f.Player.StopCount);
        var completion = f.Session.CompleteAsync(); Assert.False(completion.IsCompleted);
        b.Complete(); var last = await f.Player.NextAsync(); Assert.False(completion.IsCompleted);
        last.Exit.SetResult(true); await completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(f.Session.Snapshot.Completed);
    }
}
