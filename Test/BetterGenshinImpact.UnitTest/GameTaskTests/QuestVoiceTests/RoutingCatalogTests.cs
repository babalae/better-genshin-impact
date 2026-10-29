using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：RoutingCatalogTests。</summary>
public sealed class RoutingCatalogTests
{
    /// <summary>移植回归验证：Reads exact bytes and resolves paths against catalog directory。</summary>
    [Fact]
    public async Task Reads_exact_bytes_and_resolves_paths_against_catalog_directory()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        var bytes = await File.ReadAllBytesAsync(fixture.CatalogPath);
        var loaded = await new SpeakerCatalogLoader().LoadAsync(fixture.CatalogPath);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), loaded.CatalogSha256);
        Assert.Equal(fixture.CatalogPath, loaded.CatalogPath);
        Assert.Equal(fixture.Asset("ganyu.ckpt"), loaded.Catalog.Voices[0].GptWeightsPath);
        Assert.Throws<NotSupportedException>(() => ((IList<RoutingVoice>)loaded.Catalog.Voices).Clear());
        await File.WriteAllTextAsync(fixture.CatalogPath, "{}");
        Assert.Equal("甘雨", loaded.Catalog.Speakers[0].Names[0]);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), loaded.CatalogSha256);
    }

    /// <summary>移植回归验证：Bad root and duplicate json fields fail。</summary>
    [Theory]
    [InlineData("[]", "Catalog")]
    [InlineData("null", "Catalog")]
    [InlineData("{", "Catalog")]
    [InlineData("{\"Version\":1,\"Version\":2}", "Version")]
    public async Task Bad_root_and_duplicate_json_fields_fail(string json, string field)
    {
        using var fixture = new RoutingFixture();
        await File.WriteAllTextAsync(fixture.CatalogPath, json);
        await AssertBadAsync(fixture, field);
    }

    /// <summary>移植回归验证：Wrong fields and dangling references fail。</summary>
    [Theory]
    [InlineData("Version", "3")]
    [InlineData("Version", "\"1\"")]
    [InlineData("Game", "\"starrail\"")]
    [InlineData("PlayerNames", "{}")]
    [InlineData("PlayerNames", "[null]")]
    [InlineData("ProtagonistId", "false")]
    [InlineData("ProtagonistId", "\"missing\"")]
    [InlineData("NarratorVoiceId", "\"missing\"")]
    [InlineData("FallbackVoiceId", "\"missing\"")]
    [InlineData("MissingVoicePolicy", "\"Unknown\"")]
    [InlineData("MissingVoicePolicy", "1")]
    [InlineData("Speakers", "{}")]
    [InlineData("Voices", "[null]")]
    public async Task Wrong_fields_and_dangling_references_fail(string field, string value)
    {
        using var fixture = new RoutingFixture();
        fixture.Json[field] = JsonNode.Parse(value);
        await fixture.WriteAsync();
        await AssertBadAsync(fixture, field);
    }

    /// <summary>移植回归验证：Missing required fields fail。</summary>
    [Theory]
    [InlineData("Version")]
    [InlineData("Game")]
    [InlineData("Speakers")]
    [InlineData("Voices")]
    [InlineData("PlayerNames")]
    public async Task Missing_required_fields_fail(string field)
    {
        using var fixture = new RoutingFixture();
        fixture.Json.Remove(field);
        await fixture.WriteAsync();
        await AssertBadAsync(fixture, field);
    }

    /// <summary>移植回归验证：Duplicate ids ambiguous labels and invalid speakers fail。</summary>
    [Theory]
    [InlineData("Id", "\"genshin.ganyu\"")]
    [InlineData("Id", "\"protagonist\"")]
    [InlineData("Id", "\"narrator\"")]
    [InlineData("Id", "\"default_npc\"")]
    [InlineData("VoiceId", "\"undefined\"")]
    [InlineData("Names", "[\" Café \" ]")]
    [InlineData("Aliases", "[\"甘雨\"]")]
    [InlineData("Corrections", "[\"测试别名\"]")]
    [InlineData("Names", "[]")]
    [InlineData("Aliases", "[7]")]
    public async Task Duplicate_ids_ambiguous_labels_and_invalid_speakers_fail(string field, string value)
    {
        using var fixture = new RoutingFixture();
        fixture.Json["Speakers"]![1]![field] = JsonNode.Parse(value);
        await fixture.WriteAsync();
        await AssertBadAsync(fixture, field);
    }

    /// <summary>移植回归验证：Invalid voice fields fail。</summary>
    [Theory]
    [InlineData("Id", "\"voice.ganyu\"")]
    [InlineData("Id", "\" CONFIGURED \"")]
    [InlineData("Name", "false")]
    [InlineData("PromptText", "\" \"")]
    [InlineData("RequestedEmotion", "[]")]
    [InlineData("ReferenceEmotion", "12")]
    public async Task Invalid_voice_fields_fail(string field, string value)
    {
        using var fixture = new RoutingFixture();
        fixture.Json["Voices"]![1]![field] = JsonNode.Parse(value);
        await fixture.WriteAsync();
        await AssertBadAsync(fixture, field);
    }

    /// <summary>移植回归验证：Invalid relative asset paths fail。</summary>
    [Theory]
    [InlineData("assets/CON.txt")]
    [InlineData("assets/LPT¹.wav")]
    [InlineData("assets/bad:name")]
    [InlineData("assets/trailing.")]
    [InlineData("assets/trailing ")]
    [InlineData("assets//file")]
    [InlineData("assets/*.wav")]
    public async Task Invalid_relative_asset_paths_fail(string path)
    {
        using var fixture = new RoutingFixture();
        fixture.Json["Voices"]![0]!["GptWeightsPath"] = path;
        await fixture.WriteAsync();
        await AssertBadAsync(fixture, "GptWeightsPath");
    }

    /// <summary>移植回归验证：Runtime absolute and drive relative asset paths fail。</summary>
    [Fact]
    public async Task Runtime_absolute_and_drive_relative_asset_paths_fail()
    {
        using var fixture = new RoutingFixture();
        foreach (var path in new[] { fixture.Asset("ganyu.ckpt"), Path.GetPathRoot(fixture.Root)![..2] + "relative.ckpt" })
        {
            fixture.Json["Voices"]![0]!["GptWeightsPath"] = path;
            await fixture.WriteAsync();
            await AssertBadAsync(fixture, "GptWeightsPath");
        }
    }

    /// <summary>移植回归验证：Nullable references and default missing voice policy are legal。</summary>
    [Fact]
    public async Task Nullable_references_and_default_missing_voice_policy_are_legal()
    {
        using var fixture = new RoutingFixture();
        fixture.Json["ProtagonistId"] = null;
        fixture.Json["NarratorVoiceId"] = null;
        fixture.Json["FallbackVoiceId"] = null;
        fixture.Json.Remove("MissingVoicePolicy");
        Assert.Equal(MissingVoicePolicy.Fallback, (await fixture.CatalogAsync()).MissingVoicePolicy);
    }

    /// <summary>移植回归验证：Missing catalog and cancelled load are distinct。</summary>
    [Fact]
    public async Task Missing_catalog_and_cancelled_load_are_distinct()
    {
        using var fixture = new RoutingFixture();
        await AssertBadAsync(fixture, "speakers.json");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SpeakerCatalogLoader().LoadAsync(fixture.CatalogPath, new(true)));
    }

    /// <summary>移植回归验证：AssertBadAsync。</summary>
    private static async Task AssertBadAsync(RoutingFixture fixture, string field)
    {
        var exception = await Assert.ThrowsAsync<VoiceException>(() => new SpeakerCatalogLoader().LoadAsync(fixture.CatalogPath));
        Assert.Equal(VoiceErrorCode.Configuration, exception.Code);
        Assert.Contains(field, exception.Message);
        Assert.Contains(fixture.CatalogPath, exception.Message);
    }
}
