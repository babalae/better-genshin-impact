using System;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>新增引擎的注册、独立实例和持久化，不能覆盖旧声线。</summary>
public sealed class ExtraTtsConfigurationTests
{
    /// <summary>每个注册条目必须有真实实现及普通完整音频能力。</summary>
    [Theory]
    [InlineData("windows-sapi")]
    [InlineData("voicevox")]
    [InlineData("vits-simple-api")]
    [InlineData("openai-speech")]
    [InlineData("google-translate")]
    public void RegisteredProviderHasImplementation(string id)
    {
        using var provider = TtsProviderRegistry.Create(id);
        Assert.Equal(id, provider.Info.Id);
        Assert.True(provider.Info.Capabilities.PresetVoices);
        Assert.False(provider.Info.Capabilities.Streaming);
        Assert.False(provider.Info.Capabilities.RequiresDrain);
    }

    /// <summary>JSON重启后保留两端地址和专属参数，profile声音不受默认声音影响。</summary>
    [Fact]
    public void IndependentInstancesSurviveRestartAndProfileSnapshot()
    {
        var config = JsonConvert.DeserializeObject<QuestVoiceConfig>("""
            {"ProviderId":"voicevox","ServiceUrl":"invalid-gpt","GptWeightsPath":"keep.ckpt",
             "ExtraProviders":{"voicevox":{"LocalUrl":"http://localhost:50021/proxy/","RemoteUrl":"https://voice.example/engine/",
               "VoiceId":"8","Rate":1.2,"Pitch":0.05},"openai-speech":{"Model":"custom","VoiceId":"reader"}}}
            """)!;
        var restored = JsonConvert.DeserializeObject<QuestVoiceConfig>(JsonConvert.SerializeObject(config))!;
        var factory = new QuestVoiceSessionFactory(restored, AppContext.BaseDirectory);
        var settings = factory.CreateTtsSettings();
        Assert.Equal("http://localhost:50021/proxy/", settings.TtsServiceUrl.AbsoluteUri);
        Assert.Equal("8", settings.Provider!.VoiceId);
        Assert.Equal(1.2, JObject.FromObject(settings)["Extra"]!["Rate"]!.Value<double>());
        var profile = new RoutingVoice("vv", "声音", "", "", "", "", ProviderId: "voicevox", InstanceId: "remote", ProviderVoiceId: "10");
        var selected = factory.CreateVoiceSettings(profile);
        Assert.Equal("https://voice.example/engine/", selected.TtsServiceUrl.AbsoluteUri);
        Assert.Equal("10", selected.Provider!.VoiceId);
        Assert.Equal("keep.ckpt", restored.GptWeightsPath);
        Assert.Equal("8", factory.CreateTtsSettings().Provider!.VoiceId);
        Assert.Equal("custom", JObject.FromObject(restored)["ExtraProviders"]!["openai-speech"]!["Model"]!.Value<string>());
        Assert.False(selected.Protocol!.StreamingMode);
    }

    /// <summary>旧配置完全保留，读取新增配置不要求网络或GPT地址有效。</summary>
    [Fact]
    public void LegacySettingsRemainIntact()
    {
        var config = JsonConvert.DeserializeObject<QuestVoiceConfig>("{\"HuoshanVoiceId\":\"zh_male_zhubo\",\"RemoteServiceUrl\":\"https://old/\"}")!;
        Assert.Equal("gpt-sovits", config.ProviderId);
        Assert.Equal("zh_male_zhubo", config.HuoshanVoiceId);
        Assert.Equal("https://old/", config.RemoteServiceUrl);
        Assert.Contains(TtsProviderRegistry.Providers, x => x.Id == "voicevox");
    }

    /// <summary>生产宿主的STJ序列化与递归追踪必须识别新增的嵌套字典设置。</summary>
    [Fact]
    public void HostSerializerAndChangeTrackerKeepNestedSettings()
    {
        var all = new AllConfig();
        var changes = 0;
        all.OnAnyChangedAction = () => changes++;
        all.InitEvent();
        all.QuestVoiceConfig.ExtraProviders["openai-speech"].Model = "local-reader";
        Assert.True(changes > 0);
        var serialized = System.Text.Json.JsonSerializer.Serialize(all.QuestVoiceConfig, BetterGenshinImpact.Service.ConfigService.JsonOptions);
        var restored = System.Text.Json.JsonSerializer.Deserialize<QuestVoiceConfig>(serialized, BetterGenshinImpact.Service.ConfigService.JsonOptions)!;
        Assert.Equal("local-reader", restored.ExtraProviders["openai-speech"].Model);
        all.QuestVoiceConfig.ExtraProviders = new();
        var factory = new QuestVoiceSessionFactory(all.QuestVoiceConfig, AppContext.BaseDirectory);
        var added = factory.ExtraConfig("voicevox")!;
        var before = changes;
        added.Rate = 1.3;
        Assert.True(changes > before);
    }
}
