using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：SpeakerRouterTests。</summary>
public sealed class SpeakerRouterTests
{
    /// <summary>移植回归验证：Catalog。</summary>
    private static RoutingCatalog Catalog(string? protagonist = "genshin.aether", string[]? players = null) => new(
        1, "genshin", protagonist, players ?? ["旅行者"], null, null, MissingVoicePolicy.Fallback,
        [new("genshin.ganyu", ["甘雨", "Ganyu", "Café"], ["测试别名"], ["测试错字"], null),
         new("genshin.aether", ["空"], [], [], null)], []);

    /// <summary>移植回归验证：Strict layers and name evidence。</summary>
    [Theory]
    [InlineData("甘雨", SpeakerNameStatus.Accepted, DialogueContext.Protagonist, RoutingLayer.Protagonist, "genshin.aether", "ProtagonistContext")]
    [InlineData("旅行者", SpeakerNameStatus.Accepted, DialogueContext.Narration, RoutingLayer.Protagonist, "genshin.aether", "PlayerName")]
    [InlineData(null, SpeakerNameStatus.Missing, DialogueContext.Narration, RoutingLayer.Narrator, "narrator", "NarrationContext")]
    [InlineData("甘雨", SpeakerNameStatus.Rejected, DialogueContext.Narration, RoutingLayer.Narrator, "narrator", "NarrationContext")]
    [InlineData(" 甘雨 ", SpeakerNameStatus.Accepted, DialogueContext.Dialogue, RoutingLayer.Canonical, "genshin.ganyu", "ExactName")]
    [InlineData("gAnYu", SpeakerNameStatus.Accepted, DialogueContext.Dialogue, RoutingLayer.Canonical, "genshin.ganyu", "ExactName")]
    [InlineData(" Cafe\u0301 ", SpeakerNameStatus.Accepted, DialogueContext.Dialogue, RoutingLayer.Canonical, "genshin.ganyu", "ExactName")]
    [InlineData("测试别名", SpeakerNameStatus.Accepted, DialogueContext.Dialogue, RoutingLayer.Alias, "genshin.ganyu", "ExactAlias")]
    [InlineData("测试错字", SpeakerNameStatus.Accepted, DialogueContext.Dialogue, RoutingLayer.Correction, "genshin.ganyu", "ExactCorrection")]
    [InlineData("甘雨与刻晴", SpeakerNameStatus.Accepted, DialogueContext.Dialogue, RoutingLayer.Fallback, "default_npc", "UnknownName")]
    [InlineData("甘雨.", SpeakerNameStatus.Accepted, DialogueContext.Dialogue, RoutingLayer.Fallback, "default_npc", "UnknownName")]
    [InlineData("测试错", SpeakerNameStatus.Accepted, DialogueContext.Dialogue, RoutingLayer.Fallback, "default_npc", "UnknownName")]
    [InlineData("旅行者", SpeakerNameStatus.Rejected, DialogueContext.Dialogue, RoutingLayer.Fallback, "default_npc", "RejectedName")]
    [InlineData("甘雨", SpeakerNameStatus.Rejected, DialogueContext.Dialogue, RoutingLayer.Fallback, "default_npc", "RejectedName")]
    [InlineData("甘雨", SpeakerNameStatus.Missing, DialogueContext.Dialogue, RoutingLayer.Fallback, "default_npc", "MissingName")]
    [InlineData(null, SpeakerNameStatus.Missing, DialogueContext.Dialogue, RoutingLayer.Fallback, "default_npc", "MissingName")]
    [InlineData(null, SpeakerNameStatus.Accepted, DialogueContext.Dialogue, RoutingLayer.Fallback, "default_npc", "MissingName")]
    [InlineData(" \t", SpeakerNameStatus.Accepted, DialogueContext.Dialogue, RoutingLayer.Fallback, "default_npc", "MissingName")]
    public void Strict_layers_and_name_evidence(string? name, SpeakerNameStatus status, DialogueContext context,
        RoutingLayer layer, string id, string reason)
    {
        var route = new SpeakerRouter().Route(new(name, status, context), Catalog());
        Assert.Equal(new SpeakerRoute(layer, id, null, reason), route);
    }

    /// <summary>移植回归验证：Player name intentionally overrides canonical name。</summary>
    [Fact]
    public void Player_name_intentionally_overrides_canonical_name()
    {
        var route = new SpeakerRouter().Route(new(" 甘雨 ", SpeakerNameStatus.Accepted), Catalog(players: ["甘雨"]));
        Assert.Equal(RoutingLayer.Protagonist, route.Layer);
        Assert.Equal("genshin.aether", route.CanonicalId);
    }

    /// <summary>移植回归验证：Unconfigured protagonist never guesses gender。</summary>
    [Theory]
    [InlineData("旅行者", DialogueContext.Dialogue)]
    [InlineData("甘雨", DialogueContext.Protagonist)]
    public void Unconfigured_protagonist_never_guesses_gender(string name, DialogueContext context)
    {
        var route = new SpeakerRouter().Route(new(name, SpeakerNameStatus.Accepted, context), Catalog(protagonist: null));
        Assert.Equal(new(RoutingLayer.Protagonist, "protagonist", null, "ProtagonistNotConfigured"), route);
    }

    /// <summary>移植回归验证：Undefined input enum is diagnostic。</summary>
    [Theory]
    [InlineData(99, 0)]
    [InlineData(0, 99)]
    public void Undefined_input_enum_is_diagnostic(int nameStatus, int context)
    {
        var exception = Assert.Throws<VoiceException>(() => new SpeakerRouter().Route(
            new("甘雨", (SpeakerNameStatus)nameStatus, (DialogueContext)context), Catalog()));
        Assert.Equal(VoiceErrorCode.Configuration, exception.Code);
        Assert.Contains("Input", exception.Message);
    }

    /// <summary>移植回归验证：Caller owned lists cannot change catalog and exposed lists are read only。</summary>
    [Fact]
    public void Caller_owned_lists_cannot_change_catalog_and_exposed_lists_are_read_only()
    {
        string[] names = ["甘雨"];
        string[] players = ["旅行者"];
        var speakers = new List<SpeakerDefinition> { new("genshin.ganyu", names, [], [], null) };
        var catalog = new RoutingCatalog(1, "genshin", null, players, null, null, MissingVoicePolicy.Fallback, speakers, []);
        names[0] = "刻晴";
        players[0] = "甘雨";
        speakers.Clear();
        Assert.Equal(RoutingLayer.Canonical, new SpeakerRouter().Route(new("甘雨", SpeakerNameStatus.Accepted), catalog).Layer);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)catalog.PlayerNames)[0] = "甘雨");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)catalog.Speakers[0].Names)[0] = "刻晴");
        Assert.Throws<NotSupportedException>(() => ((IList<SpeakerDefinition>)catalog.Speakers).Clear());
    }

    /// <summary>移植回归验证：Externally constructed ambiguous catalog is rejected before matching。</summary>
    [Fact]
    public void Externally_constructed_ambiguous_catalog_is_rejected_before_matching()
    {
        var catalog = new RoutingCatalog(1, "genshin", null, [], null, null, MissingVoicePolicy.Fallback,
            [new("a", ["甘雨"], [], [], null), new("b", [" 甘雨 "], [], [], null)], []);
        var exception = Assert.Throws<VoiceException>(() => new SpeakerRouter().Route(new("甘雨", SpeakerNameStatus.Accepted), catalog));
        Assert.Equal(VoiceErrorCode.Configuration, exception.Code);
        Assert.Contains("Names", exception.Message);
    }
}
