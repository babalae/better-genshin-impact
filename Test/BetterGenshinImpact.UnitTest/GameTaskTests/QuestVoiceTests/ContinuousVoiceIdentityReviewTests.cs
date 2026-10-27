using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousVoiceIdentityReviewTests。</summary>
public sealed class ContinuousVoiceIdentityReviewTests
{
    /// <summary>移植回归验证：Canonical ID containing unknown name prefix is distinct from unknown speaker。</summary>
    [Fact]
    public async Task Canonical_ID_containing_unknown_name_prefix_is_distinct_from_unknown_speaker()
    {
        await using var f = new ContinuousVoiceFixture();
        f.Catalog.Json["Speakers"]![1]!["Id"] = "default_npc:Visitor";
        f.Catalog.Json["Speakers"]![1]!["Names"]![0] = "Catalog Hero";
        await f.StartAsync();
        var first = await f.PlayAsync(1, "same line", "Catalog Hero"); first.Exit.SetResult(true);
        await f.EventAsync(ContinuousVoiceEventKind.PlaybackCompleted, 1);
        var firstWork = (await f.EventAsync(ContinuousVoiceEventKind.Routed, 1)).Work!;
        var decision = await DecideAsync(f, 2, "same line", "Visitor");
        var secondWork = (await f.EventAsync(ContinuousVoiceEventKind.Routed, 2)).Work!;
        Assert.Equal("default_npc:Visitor", firstWork.Routing!.Speaker.CanonicalId);
        Assert.Equal("default_npc", secondWork.Routing!.Speaker.CanonicalId);
        Assert.Equal(firstWork.Routing.VoiceId, secondWork.Routing.VoiceId);
        Assert.NotEqual(firstWork.Identity, secondWork.Identity);
        Assert.Equal(ContinuousVoiceEventKind.SynthesisStarted, decision.Kind);
        (await f.Engine.NextAsync()).Complete(); var second = await f.Player.NextAsync(); second.Exit.SetResult(true);
        await f.Session.CompleteAsync(); Assert.Equal(2, f.Player.All.Count);
    }

    /// <summary>未具名 NPC 的姓名抖动不改变同一 fallback 声线所有权，不能重启同句合成。</summary>
    [Fact]
    public async Task Unknown_speaker_name_flicker_does_not_restart_same_line()
    {
        await using var f = new ContinuousVoiceFixture();
        await f.StartAsync();
        var first = await f.PlayAsync(1, "same line", "Visitor");
        first.Exit.TrySetResult(true);
        await f.EventAsync(ContinuousVoiceEventKind.PlaybackCompleted, 1);
        f.Session.Invalidate(2);
        var missing = ContinuousVoiceFixture.Candidate(2, "same line", "Visitor") with
        {
            AcceptedName = null,
            NameStatus = SpeakerNameStatus.Missing,
            SemanticGeneration = 2
        };
        Assert.True(await f.Session.MatchesCurrentDialogueAsync(missing));
        await f.Session.CompleteAsync();
    }

    /// <summary>移植回归验证：Accepted name identity uses routing case and Unicode semantics。</summary>
    [Theory]
    [InlineData(true, "Visitor", " visitor ")]
    [InlineData(false, "Visitor", " visitor ")]
    [InlineData(true, "Åke", " a\u030ake ")]
    [InlineData(false, "Åke", " a\u030ake ")]
    public async Task Accepted_name_identity_uses_routing_case_and_Unicode_semantics(bool routed, string original, string recheck)
    {
        await using var f = new ContinuousVoiceFixture();
        await f.StartAsync(settings: routed ? f.Settings : f.Settings with { Routing = null });
        var first = await f.PlayAsync(1, "same line", original); first.Exit.SetResult(true);
        await f.EventAsync(ContinuousVoiceEventKind.PlaybackCompleted, 1);
        Assert.Equal(ContinuousVoiceEventKind.Duplicate, (await DecideAsync(f, 2, "same line", recheck)).Kind);
        await f.Session.CompleteAsync(); Assert.Single(f.Engine.All); Assert.Single(f.Player.All);
        if (routed)
            Assert.Equal("default_npc", (await f.EventAsync(ContinuousVoiceEventKind.Routed, 2)).Work!.Routing!.Speaker.CanonicalId);
        else Assert.Empty(f.Routing.All);
    }

    /// <summary>移植回归验证：Canonical ID case only catalog update keeps same identity。</summary>
    [Fact]
    public async Task Canonical_ID_case_only_catalog_update_keeps_same_identity()
    {
        await using var f = new ContinuousVoiceFixture();
        f.Catalog.Json["Speakers"]![1]!["Id"] = "default_npc:Visitor";
        await f.StartAsync();
        var first = await f.PlayAsync(1, "same line", "刻晴"); first.Exit.SetResult(true);
        await f.EventAsync(ContinuousVoiceEventKind.PlaybackCompleted, 1);
        f.Catalog.Json["Speakers"]![1]!["Id"] = "DEFAULT_NPC:VISITOR";
        await f.Catalog.WriteAsync();
        var decision = await DecideAsync(f, 2, "same line", "刻晴");
        var firstWork = (await f.EventAsync(ContinuousVoiceEventKind.Routed, 1)).Work!;
        var secondWork = (await f.EventAsync(ContinuousVoiceEventKind.Routed, 2)).Work!;
        Assert.Equal("default_npc:Visitor", firstWork.Routing!.Speaker.CanonicalId);
        Assert.Equal("DEFAULT_NPC:VISITOR", secondWork.Routing!.Speaker.CanonicalId);
        Assert.NotEqual(firstWork.Routing.CatalogSha256, secondWork.Routing.CatalogSha256);
        Assert.Equal(ContinuousVoiceEventKind.Duplicate, decision.Kind);
        Assert.Equal(firstWork.Identity, secondWork.Identity);
        Assert.Equal(firstWork.Identity!.GetHashCode(), secondWork.Identity!.GetHashCode());
        await f.Session.CompleteAsync(); Assert.Single(f.Engine.All);
    }

    /// <summary>移植回归验证：Identity case insensitivity does not fold dialogue case。</summary>
    [Fact]
    public async Task Identity_case_insensitivity_does_not_fold_dialogue_case()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var first = await f.PlayAsync(1, "Hi", "Visitor"); first.Exit.SetResult(true);
        await f.EventAsync(ContinuousVoiceEventKind.PlaybackCompleted, 1);
        Assert.Equal(ContinuousVoiceEventKind.SynthesisStarted, (await DecideAsync(f, 2, "hi", "visitor")).Kind);
        (await f.Engine.NextAsync()).Complete(); var second = await f.Player.NextAsync(); second.Exit.SetResult(true);
        await f.Session.CompleteAsync(); Assert.Equal(new[] { "Hi", "hi" }, f.Engine.All.Select(call => call.Request.Text));
    }

    /// <summary>移植回归验证：DecideAsync。</summary>
    private static async Task<ContinuousVoiceEvent> DecideAsync(ContinuousVoiceFixture f, long revision, string text, string name)
    {
        var decision = ContinuousVoiceFixture.NewCompletion<ContinuousVoiceEvent>();
        void Observe(ContinuousVoiceEvent value)
        {
            if (value.Revision == revision && value.Kind is ContinuousVoiceEventKind.Duplicate or ContinuousVoiceEventKind.SynthesisStarted)
                decision.TrySetResult(value);
        }
        f.Session.Event += Observe;
        try { f.Offer(revision, text, name); return await decision.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { f.Session.Event -= Observe; }
    }
}
