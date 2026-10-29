using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：RoutingFixture。</summary>
internal sealed class RoutingFixture : IDisposable
{
    /// <summary>移植回归验证：Root。</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "QuestVoiceRoutingTests", Guid.NewGuid().ToString("N"));
    /// <summary>移植回归验证：CatalogPath。</summary>
    public string CatalogPath => Path.Combine(Root, "catalog", "speakers.json");
    /// <summary>移植回归验证：Json。</summary>
    public JsonObject Json { get; } = JsonNode.Parse("""
        {
          "Version": 1, "Game": "genshin", "ProtagonistId": "genshin.aether", "PlayerNames": ["旅行者"],
          "NarratorVoiceId": "voice.narrator", "FallbackVoiceId": "voice.fallback", "MissingVoicePolicy": "Fallback",
          "Speakers": [
            { "Id": "genshin.ganyu", "Names": ["甘雨", "Ganyu", "Café"], "Aliases": ["测试别名"], "Corrections": ["测试错字"], "VoiceId": "voice.ganyu" },
            { "Id": "genshin.keqing", "Names": ["刻晴"], "Aliases": [], "Corrections": [], "VoiceId": null },
            { "Id": "genshin.aether", "Names": ["空"], "Aliases": [], "Corrections": [], "VoiceId": "voice.ganyu" }
          ],
          "Voices": [
            { "Id": "voice.ganyu", "Name": "甘雨", "GptWeightsPath": "../assets/ganyu.ckpt", "SovitsWeightsPath": "../assets/ganyu.pth", "ReferenceAudioPath": "../assets/ganyu.wav", "PromptText": "参考原文。", "RequestedEmotion": "平静", "ReferenceEmotion": "平静" },
            { "Id": "voice.fallback", "Name": "通用声线", "GptWeightsPath": "../assets/fallback.ckpt", "SovitsWeightsPath": "../assets/fallback.pth", "ReferenceAudioPath": "../assets/fallback.wav", "PromptText": "通用参考。" },
            { "Id": "voice.narrator", "Name": "旁白声线", "GptWeightsPath": "../assets/narrator.ckpt", "SovitsWeightsPath": "../assets/narrator.pth", "ReferenceAudioPath": "../assets/narrator.wav", "PromptText": "旁白参考。" }
          ]
        }
        """)!.AsObject();
    /// <summary>移植回归验证：LegacyVoice。</summary>
    public VoiceSettings LegacyVoice => new("历史声线", Asset("legacy.ckpt"), Asset("legacy.pth"),
        Asset("legacy.wav"), "历史参考。", "轻松", "轻松");

    /// <summary>移植回归验证：RoutingFixture。</summary>
    public RoutingFixture()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
        Directory.CreateDirectory(Path.Combine(Root, "assets"));
        foreach (var name in new[] { "ganyu", "fallback", "narrator", "legacy" })
            foreach (var suffix in new[] { ".ckpt", ".pth", ".wav" })
                File.WriteAllBytes(Asset(name + suffix), [1, 2, 3]);
    }

    /// <summary>移植回归验证：Asset。</summary>
    public string Asset(string name) => Path.Combine(Root, "assets", name);
    /// <summary>移植回归验证：WriteAsync。</summary>
    public Task WriteAsync() => File.WriteAllTextAsync(CatalogPath, Json.ToJsonString());
    /// <summary>移植回归验证：CatalogAsync。</summary>
    public async Task<RoutingCatalog> CatalogAsync()
    {
        await WriteAsync();
        return (await new SpeakerCatalogLoader().LoadAsync(CatalogPath)).Catalog;
    }
    /// <summary>按接受姓名解析角色和实际声线，保留兜底原因。</summary>
    public async Task<VoiceRoutingResult> ResolveAsync(SpeakerRoutingInput? input = null, CancellationToken token = default)
    {
        await WriteAsync();
        return await new VoiceRoutingService(new SpeakerCatalogLoader(), new SpeakerRouter()).ResolveAsync(
            new(new(CatalogPath), input ?? new("甘雨", SpeakerNameStatus.Accepted), LegacyVoice), token);
    }
    /// <summary>释放本实例拥有的本地资源。</summary>
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
