using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousVoiceCancellationTests。</summary>
public sealed class ContinuousVoiceCancellationTests
{
    /// <summary>移植回归验证：Ordinary late failure is stale evidence without overwriting current snapshot。</summary>
    [Theory]
    [InlineData("route")]
    [InlineData("synthesis")]
    [InlineData("save")]
    public async Task Ordinary_late_failure_is_stale_evidence_without_overwriting_current_snapshot(string boundary)
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); await f.Routing.NextAsync(); await f.Artifacts.NextAsync();
        f.Routing.Hold = boundary == "route"; f.Artifacts.Hold = boundary == "save";
        var terminal = ContinuousVoiceFixture.NewCompletion<ContinuousVoiceEvent>();
        f.Session.Event += e => { if (e.Revision == 2 && e.Kind is ContinuousVoiceEventKind.Discarded or ContinuousVoiceEventKind.CandidateFailed) terminal.TrySetResult(e); };
        f.Offer(2, "B");
        var route = await f.Routing.NextAsync();
        ContinuousVoiceFixture.Synthesis? synthesis = null; ContinuousVoiceFixture.Save? save = null;
        if (boundary != "route") synthesis = await f.Engine.NextAsync();
        if (boundary == "save") { synthesis!.Complete(); save = await f.Artifacts.NextAsync(); }
        f.Offer(3, "C");
        var failure = new BetterGenshinImpact.GameTask.QuestVoice.Core.Tts.VoiceException(BetterGenshinImpact.GameTask.QuestVoice.Core.Tts.VoiceErrorCode.SynthesisRejected, "stale failure");
        if (boundary == "route") route.Release.SetException(failure);
        else if (boundary == "save") save!.Result.SetException(failure);
        else synthesis!.Result.SetException(failure);
        var result = await terminal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ContinuousVoiceEventKind.Discarded, result.Kind); Assert.Equal(failure.Code, result.Error);
        Assert.Null(f.Session.Snapshot.Error); Assert.False(a.Token.IsCancellationRequested);
    }

    /// <summary>移植回归验证：Superseding after Stop was requested never starts stale B。</summary>
    [Fact]
    public async Task Superseding_after_Stop_was_requested_never_starts_stale_B()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); f.Offer(2, "B"); (await f.Engine.NextAsync()).Complete();
        await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10));
        f.Offer(3, "C"); var c = await f.Engine.NextAsync(); c.Complete();
        await f.EventAsync(ContinuousVoiceEventKind.AudioReady, 3);
        Assert.Single(f.Player.All); a.Exit.SetResult(true);
        Assert.Equal("C", (await f.Player.NextAsync()).Text);
    }

    /// <summary>移植回归验证：Stop during replacement gap drains old player without starting B。</summary>
    [Fact]
    public async Task Stop_during_replacement_gap_drains_old_player_without_starting_B()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); f.Offer(2, "B"); (await f.Engine.NextAsync()).Complete();
        await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stop = f.Session.StopAsync(); Assert.False(stop.IsCompleted);
        a.Exit.SetResult(true); await stop.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(f.Player.All); Assert.True(f.Session.Snapshot.Completed);
    }

    /// <summary>移植回归验证：Stop and dispose wait for noncooperative synthesis。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stop_and_dispose_wait_for_noncooperative_synthesis(bool dispose)
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); f.Offer(2, "B"); var b = await f.Engine.NextAsync();
        var stopping = dispose ? f.Session.DisposeAsync().AsTask() : f.Session.StopAsync();
        Assert.True(a.Token.IsCancellationRequested); Assert.True(b.Token.IsCancellationRequested);
        Assert.False(stopping.IsCompleted); a.Exit.SetResult(true); Assert.False(stopping.IsCompleted);
        b.Complete(); await stopping.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(f.Artifacts.All); Assert.Single(f.Player.All); Assert.Equal(0, f.Engine.Shutdowns);
    }

    /// <summary>移植回归验证：Late save is explicitly discarded with artifact and cannot stop A。</summary>
    [Fact]
    public async Task Late_save_is_explicitly_discarded_with_artifact_and_cannot_stop_A()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); await f.Artifacts.NextAsync(); f.Artifacts.Hold = true;
        f.Offer(2, "B"); (await f.Engine.NextAsync()).Complete(); var save = await f.Artifacts.NextAsync();
        f.Session.Invalidate(3); save.Complete();
        var discarded = await f.EventAsync(ContinuousVoiceEventKind.Discarded, 2);
        Assert.Equal("saved.wav", discarded.Work!.ArtifactPath); Assert.False(a.Token.IsCancellationRequested);
        a.Exit.SetResult(true); await f.Session.CompleteAsync();
        Assert.DoesNotContain(f.Events, e => e.Revision == 2 && e.Kind == ContinuousVoiceEventKind.AudioReady);
    }

    /// <summary>移植回归验证：Late routing is discarded before tts。</summary>
    [Fact]
    public async Task Late_routing_is_discarded_before_tts()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync(); f.Routing.Hold = true;
        f.Offer(1, "old"); var route = await f.Routing.NextAsync(); f.Offer(2, "new");
        Assert.True(route.Token.IsCancellationRequested); route.Release.SetResult(true);
        var next = await f.Routing.NextAsync(); next.Release.SetResult(true);
        Assert.Equal("new", (await f.Engine.NextAsync()).Request.Text); Assert.Single(f.Engine.All);
    }

    /// <summary>移植回归验证：Synchronous event dispose is owned and prevents subsequent publication。</summary>
    [Theory]
    [InlineData(ContinuousVoiceEventKind.Routed)]
    [InlineData(ContinuousVoiceEventKind.SynthesisCompleted)]
    [InlineData(ContinuousVoiceEventKind.Saving)]
    [InlineData(ContinuousVoiceEventKind.AudioReady)]
    public async Task Synchronous_event_dispose_is_owned_and_prevents_subsequent_publication(ContinuousVoiceEventKind boundary)
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync(); Task? disposing = null;
        f.Session.Event += value => { if (value.Kind == boundary) disposing = f.Session.DisposeAsync().AsTask(); };
        f.Offer(1, "A");
        if (boundary != ContinuousVoiceEventKind.Routed) (await f.Engine.NextAsync()).Complete();
        await f.EventAsync(boundary, 1);
        await f.Session.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(disposing); await disposing!.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Empty(f.Player.All);
    }

    /// <summary>移植回归验证：Callback can invalidate and offer new candidate during audio ready。</summary>
    [Fact]
    public async Task Callback_can_invalidate_and_offer_new_candidate_during_audio_ready()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        f.Session.Event += value => { if (value.Kind == ContinuousVoiceEventKind.AudioReady && value.Revision == 1) f.Offer(2, "B"); };
        f.Offer(1, "A"); (await f.Engine.NextAsync()).Complete();
        var b = await f.Engine.NextAsync(); Assert.Equal("B", b.Request.Text); b.Complete();
        Assert.Equal("B", (await f.Player.NextAsync()).Text); Assert.Single(f.Player.All);
    }
}
