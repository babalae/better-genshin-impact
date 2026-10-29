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

/// <summary>移植回归验证：ContinuousVoiceIdentityTests。</summary>
public sealed class ContinuousVoiceIdentityTests
{
    /// <summary>同一语义的视觉来源可以变化，合成中和Ready时重复Offer均不重入。</summary>
    [Fact] public async Task SemanticGenerationOwnsSynthesisAndReadyAcrossVisualRevisions()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        using var lease = f.Session.BeginObservation();
        f.Session.Invalidate(1);
        var candidate = ContinuousVoiceFixture.Candidate(10, "same") with { SemanticGeneration = 1 };
        f.Session.Offer(candidate); var first = await f.Engine.NextAsync();
        f.Session.Offer(candidate with { Revision = 11 });
        Assert.False(first.Token.IsCancellationRequested);
        first.Complete(); await f.EventAsync(ContinuousVoiceEventKind.AudioReady, 1);
        f.Session.Offer(candidate with { Revision = 12 });
        Assert.Equal(10, f.Session.Snapshot.Ready!.Candidate.Revision);
        Assert.Single(f.Engine.All);
        lease.Dispose(); var playing = await f.Player.NextAsync();
        playing.Exit.TrySetResult(true); await f.Session.CompleteAsync();
    }
    /// <summary>旧HTTP尚在排空时，新的Pending也可只读核对别名，不发并行合成。</summary>
    [Fact] public async Task RouteComparisonCoversUnresolvedPendingWithoutIssuingHttp()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        f.Offer(1, "old"); var old = await f.Engine.NextAsync();
        f.Offer(2, "same");
        var alias = ContinuousVoiceFixture.Candidate(20, "same", "测试别名") with { SemanticGeneration = 2 };
        Assert.True(await f.Session.MatchesCurrentDialogueAsync(alias));
        Assert.Single(f.Engine.All);
        Assert.NotNull(f.Session.Snapshot.Pending);
        Assert.True(old.Token.IsCancellationRequested);
    }

    /// <summary>别名预检在Ready和已播所有权上均保留原结果。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RouteComparisonCoversReadyAndPlayedOwnership(bool readyOnly)
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        using var lease = readyOnly ? f.Session.BeginObservation() : null;
        f.Offer(1, "same"); (await f.Engine.NextAsync()).Complete();
        if (readyOnly) await f.EventAsync(ContinuousVoiceEventKind.AudioReady, 1);
        else
        {
            (await f.Player.NextAsync()).Exit.TrySetResult(true);
            await f.EventAsync(ContinuousVoiceEventKind.PlaybackCompleted, 1);
        }
        var alias = ContinuousVoiceFixture.Candidate(20, "same", "测试别名") with { SemanticGeneration = 1 };
        Assert.True(await f.Session.MatchesCurrentDialogueAsync(alias));
        Assert.Single(f.Engine.All);
    }

    /// <summary>同canonical的实际模型改变不能复用原来冻结的声线。</summary>
    [Fact] public async Task RouteComparisonRejectsChangedActualVoiceBinding()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        f.Offer(1, "same"); var first = await f.Engine.NextAsync();
        f.Catalog.Json["Voices"]![0]!["GptWeightsPath"] = "../assets/fallback.ckpt";
        await f.Catalog.WriteAsync();
        var alias = ContinuousVoiceFixture.Candidate(20, "same", "测试别名") with { SemanticGeneration = 1 };
        Assert.False(await f.Session.MatchesCurrentDialogueAsync(alias));
        Assert.False(first.Token.IsCancellationRequested);
    }
    /// <summary>移植回归验证：Canonical alias and trim form C match without changing synthesis text。</summary>
    [Fact]
    public async Task Canonical_alias_and_trim_form_C_match_without_changing_synthesis_text()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        const string original = "  Cafe\u0301，原文！  ";
        var a = await f.PlayAsync(1, original); a.Exit.SetResult(true);
        await f.EventAsync(ContinuousVoiceEventKind.PlaybackCompleted, 1);
        f.Offer(2, "Café，原文！", "测试别名");
        await f.EventAsync(ContinuousVoiceEventKind.Duplicate, 2); await f.Session.CompleteAsync();
        Assert.Equal(original, Assert.Single(f.Engine.All).Request.Text); Assert.Single(f.Player.All);
    }

    /// <summary>移植回归验证：Different identities with same text and A B A each play。</summary>
    [Theory]
    [InlineData("甘雨", "刻晴")]
    [InlineData("陌生甲", "陌生乙")]
    public async Task Different_identities_with_same_text_and_A_B_A_each_play(string first, string second)
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        for (var revision = 1; revision <= 3; revision++)
        {
            var play = await f.PlayAsync(revision, "same", revision == 2 ? second : first);
            play.Exit.SetResult(true); await f.EventAsync(ContinuousVoiceEventKind.PlaybackCompleted, revision);
        }
        await f.Session.CompleteAsync(); Assert.Equal(3, f.Engine.All.Count);
    }

    /// <summary>移植回归验证：Accepted failed B breaks identity run and A may replay。</summary>
    [Fact]
    public async Task Accepted_failed_B_breaks_identity_run_and_A_may_replay()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); a.Exit.SetResult(true);
        await f.EventAsync(ContinuousVoiceEventKind.PlaybackCompleted, 1);
        f.Offer(2, "B"); (await f.Engine.NextAsync()).Result.SetException(new VoiceException(VoiceErrorCode.SynthesisRejected, "failure"));
        await f.EventAsync(ContinuousVoiceEventKind.CandidateFailed, 2);
        f.Offer(3, "A"); Assert.Equal("A", (await f.Engine.NextAsync()).Request.Text);
    }

    /// <summary>移植回归验证：Leave stops immediately resets epoch and allows same dialogue。</summary>
    [Fact]
    public async Task Leave_stops_immediately_resets_epoch_and_allows_same_dialogue()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); var leave = f.Session.LeaveDialogueAsync(2);
        Assert.True(a.Token.IsCancellationRequested); Assert.False(leave.IsCompleted);
        Assert.Equal(1, f.Session.Snapshot.Epoch); a.Exit.SetResult(true); await leave;
        var reentry = await f.PlayAsync(3, "A"); Assert.Equal("A", reentry.Text); Assert.Equal(2, f.Engine.All.Count);
    }

    /// <summary>移植回归验证：No routing uses configured voice and routing configuration error fails。</summary>
    [Fact]
    public async Task No_routing_uses_configured_voice_and_routing_configuration_error_fails()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync(settings: f.Settings with { Routing = null });
        f.Offer(1, "A"); var call = await f.Engine.NextAsync();
        Assert.Equal(f.Settings.Voice.Name, call.Request.Voice.Name); Assert.Empty(f.Routing.All);
        await using var bad = new ContinuousVoiceFixture();
        await bad.StartAsync(settings: bad.Settings with { Routing = null, RoutingConfigurationError = "bad routing" });
        bad.Offer(1, "A"); await bad.EventAsync(ContinuousVoiceEventKind.CandidateFailed, 1);
        Assert.Empty(bad.Engine.All);
    }
}
