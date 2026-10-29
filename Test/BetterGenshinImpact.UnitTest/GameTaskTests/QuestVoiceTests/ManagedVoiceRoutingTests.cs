using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>角色管理升级必须在真实解析及路由链中保留身份和不同引擎声音。</summary>
public sealed class ManagedVoiceRoutingTests
{
    /// <summary>预设声线不依赖 GPT 文件；同一目录可承载旅行者、角色与兜底。</summary>
    [Theory]
    [InlineData("甘雨", "genshin.ganyu", "voice.ganyu")]
    [InlineData("测试别名", "genshin.ganyu", "voice.ganyu")]
    [InlineData("测试错字", "genshin.ganyu", "voice.ganyu")]
    [InlineData("旅行者", "genshin.aether", "voice.ganyu")]
    [InlineData("未知NPC", "default_npc", "voice.fallback")]
    public async Task Version2_routes_preset_profiles_without_Gpt_assets(string name, string canonical, string voice)
    {
        using var fixture = new RoutingFixture();
        MakePresetCatalog(fixture);
        Directory.Delete(Path.Combine(fixture.Root, "assets"), true);
        var result = await fixture.ResolveAsync(new(name, SpeakerNameStatus.Accepted));
        Assert.Equal(canonical, result.Speaker.CanonicalId);
        Assert.Equal(voice, result.VoiceId);
        if (canonical == "genshin.ganyu") Assert.Equal("甘雨", result.CanonicalName);
    }

    /// <summary>荧的身份与空分离，自定义昵称仅指向选择的旅行者。</summary>
    [Fact]
    public async Task Lumine_and_Aether_keep_independent_voices_and_nickname_selection()
    {
        using var fixture = new RoutingFixture();
        MakePresetCatalog(fixture);
        fixture.Json["Speakers"]!.AsArray().Add(JsonNode.Parse("""
            {"Id":"genshin.lumine","Names":["荧"],"Aliases":[],"Corrections":[],"VoiceId":"voice.narrator"}
            """));
        fixture.Json["ProtagonistId"] = "genshin.lumine";
        fixture.Json["PlayerNames"] = new JsonArray("星海旅人", "旅行者");
        Assert.Equal("voice.narrator", (await fixture.ResolveAsync(new("星海旅人", SpeakerNameStatus.Accepted))).VoiceId);
        Assert.Equal("voice.ganyu", (await fixture.ResolveAsync(new("空", SpeakerNameStatus.Accepted))).VoiceId);
        Assert.Equal("voice.narrator", (await fixture.ResolveAsync(new("荧", SpeakerNameStatus.Accepted))).VoiceId);
    }

    /// <summary>未知或缺失预设音色必须有明确诊断，不能静默发给默认引擎。</summary>
    [Theory]
    [InlineData("ProviderId", "not-installed")]
    [InlineData("ProviderVoiceId", "")]
    [InlineData("InstanceId", "unconfigured-instance")]
    public async Task Invalid_provider_reference_is_rejected(string field, string value)
    {
        using var fixture = new RoutingFixture();
        MakePresetCatalog(fixture);
        fixture.Json["Voices"]![0]![field] = value;
        await fixture.WriteAsync();
        await Assert.ThrowsAsync<VoiceException>(() => new SpeakerCatalogLoader().LoadAsync(fixture.CatalogPath));
    }

    /// <summary>昵称不得静默抢占其他角色的规范姓名、别名或纠错名。</summary>
    [Theory]
    [InlineData("甘雨")]
    [InlineData("测试别名")]
    [InlineData("测试错字")]
    public async Task Nickname_collision_with_other_character_is_rejected(string nickname)
    {
        using var fixture = new RoutingFixture();
        MakePresetCatalog(fixture);
        fixture.Json["PlayerNames"] = new JsonArray(nickname);
        await fixture.WriteAsync();
        var error = await Assert.ThrowsAsync<VoiceException>(() => new SpeakerCatalogLoader().LoadAsync(fixture.CatalogPath));
        Assert.Contains("PlayerNames", error.Message);
    }

    /// <summary>版本二目录在默认普通 Provider 下真实启用路由，且逐profile实例覆盖全局选择。</summary>
    [Fact]
    public async Task Factory_enables_managed_routing_and_resolves_profile_instances_independently()
    {
        using var fixture = new RoutingFixture();
        MakePresetCatalog(fixture);
        await fixture.WriteAsync();
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "ocr.onnx"), [1]);
        var config = new QuestVoiceConfig { ProviderId = "huoshan", RoutingEnabled = true, RoutingCatalogPath = fixture.CatalogPath,
            OcrModelPath = "ocr.onnx", UseRemoteService = true, RemoteServiceUrl = "https://remote.example.com/api/",
            RemoteGptWeightsPath = "/unused", ServiceUrl = "http://localhost:9880/" };
        var factory = new QuestVoiceSessionFactory(config, fixture.Root);
        var validation = await factory.ValidateAsync(default);
        Assert.True(validation.IsValid, validation.Detail);
        Assert.NotNull(validation.Settings!.Routing);
        var local = factory.CreateVoiceSettings(new("local", "本机", fixture.Asset("ganyu.ckpt"), fixture.Asset("ganyu.pth"),
            fixture.Asset("ganyu.wav"), "本机原文", InstanceId: "local"));
        Assert.False(local.Voice.ServerSidePaths);
        Assert.Equal("http://localhost:9880/", local.TtsServiceUrl.AbsoluteUri);
        var remote = factory.CreateVoiceSettings(new("remote", "服务器", "/a.ckpt", "/a.pth", "/a.wav", "服务器原文", InstanceId: "remote"));
        Assert.True(remote.Voice.ServerSidePaths);
        Assert.Equal("/a.wav", remote.Voice.ReferenceAudioPath);
        Assert.Equal("https://remote.example.com/api/", remote.TtsServiceUrl.AbsoluteUri);
        Assert.Equal("huoshan", config.ProviderId);
    }

    /// <summary>制作无需本地模型的真实版本二目录输入。</summary>
    internal static void MakePresetCatalog(RoutingFixture fixture)
    {
        fixture.Json["Version"] = 2;
        foreach (var voice in fixture.Json["Voices"]!.AsArray())
        {
            var profile = voice!.AsObject();
            foreach (var field in new[] { "GptWeightsPath", "SovitsWeightsPath", "ReferenceAudioPath", "PromptText" }) profile.Remove(field);
            profile["ProviderId"] = "huoshan";
            profile["InstanceId"] = "local";
            profile["ProviderVoiceId"] = "zh_female_qingxin";
        }
    }
}
