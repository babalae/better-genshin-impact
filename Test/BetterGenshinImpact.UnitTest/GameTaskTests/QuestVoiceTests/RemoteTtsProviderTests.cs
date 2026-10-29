using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>远程服务器资源语义和独立后端错误传播。</summary>
public sealed class RemoteTtsProviderTests
{
    /// <summary>反向代理子路径和服务器文件名均应原样进入 api_v2 请求。</summary>
    [Fact]
    public async Task RemoteGptUsesServerPathsAndPreservesProxyPrefix()
    {
        var paths = new List<string>();
        var settings = new AppSettings(".", new("https://tts.example.com/proxy/"), new(1, 2, 3),
            new("远程", "/models/甘雨 & test.ckpt", "/models/ganyu.pth", "/audio/ref.wav", "参考原文", ServerSidePaths: true),
            new(".", "."), new(0, 0));
        using var provider = new GptSovitsEngine(_ => new HttpClient(new Handler(async (message, token) =>
        {
            paths.Add(message.RequestUri!.PathAndQuery);
            if (message.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = new StringContent("{\"message\":\"success\"}") };
            using var json = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(token));
            Assert.Equal("/audio/ref.wav", json.RootElement.GetProperty("ref_audio_path").GetString());
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(WavAndReferenceTests.Wave(32000, [300, -300])) };
        })), new ReferenceAudioSelector(), new WavProcessor());
        var voice = settings.Voice;
        var result = await provider.SynthesizeAsync(settings, new("你好", new(voice.Name, voice.GptWeightsPath,
            voice.SovitsWeightsPath, [new(voice.ReferenceAudioPath, voice.PromptText)])), null, default);
        Assert.Equal("/proxy/set_gpt_weights?weights_path=" + Uri.EscapeDataString(voice.GptWeightsPath), paths[0]);
        Assert.Equal("/proxy/tts", paths[2]);
        Assert.Equal(32000, result.SampleRate);
    }

    /// <summary>远程映射支持 Linux、Windows 服务器路径，不能本地 resolve 或本地检查。</summary>
    [Fact]
    public async Task RemoteRoutingPreservesServerAssets()
    {
        using var fixture = new RoutingFixture();
        foreach (var item in fixture.Json["Voices"]!.AsArray())
        {
            item!["GptWeightsPath"] = "/server/ganyu.ckpt";
            item["SovitsWeightsPath"] = "Z:\\models\\ganyu.pth";
            item["ReferenceAudioPath"] = "references/ganyu.wav";
        }
        await fixture.WriteAsync();
        var service = new VoiceRoutingService(new SpeakerCatalogLoader(), new SpeakerRouter());
        var result = await service.ResolveAsync(new(new(fixture.CatalogPath, ServerSidePaths: true),
            new("甘雨", SpeakerNameStatus.Accepted), fixture.LegacyVoice), default);
        Assert.Equal("/server/ganyu.ckpt", result.Voice.GptWeightsPath);
        Assert.Equal("Z:\\models\\ganyu.pth", result.Voice.SovitsWeightsPath);
        Assert.Equal("references/ganyu.wav", result.Voice.References[0].Path);
    }

    /// <summary>错误代码不能替代 Provider 明确的远端状态判断。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContinuousEventsPublishActualBackendState(bool backendUnknown)
    {
        await using var fixture = new ContinuousVoiceFixture();
        await fixture.StartAsync();
        fixture.Offer(1, "你好");
        var synthesis = await fixture.Engine.NextAsync();
        synthesis.Result.SetException(new VoiceException(VoiceErrorCode.Timeout, "timeout", backendUnknown));
        var failed = await fixture.EventAsync(ContinuousVoiceEventKind.CandidateFailed, 1);
        Assert.Equal(backendUnknown, failed.BackendUnknown);
        Assert.Equal(backendUnknown, fixture.Session.Snapshot.BackendUnknown);
    }

    /// <summary>只替代 HTTP 传输，所有切权/合成逻辑仍运行生产代码。</summary>
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        /// <summary>返回受控协议响应。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token) => send(message, token);
    }
}
