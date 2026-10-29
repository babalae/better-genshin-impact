using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>真实生产 Provider 的协议失败、取消和格式边界；成功音频由独立联网验收覆盖。</summary>
public sealed class HuoshanTtsProviderTests
{
    /// <summary>完全不含 GPT 文件的有效配置。</summary>
    private static AppSettings Settings => new(".", HuoshanTtsProvider.Endpoint, new(1, 1, 1),
        new("预设", "", "", "", ""), new(".", "."), new(0, 0), Provider: new("huoshan", VoiceId: "zh_female_qingxin"));
    /// <summary>普通预设不需要参考音频。</summary>
    private static TtsRequest Request => new("你好，旅行者。", new("预设", "", "", []));

    /// <summary>只声明真实能力，并按稳定 ID 返回声音。</summary>
    [Fact]
    public async Task PresetsAreAvailableWithoutNetworkOrWeights()
    {
        using var provider = Create((_, _) => throw new Exception("不应该访问网络"));
        provider.Validate(Settings);
        var voices = await provider.GetVoicesAsync(Settings, default);
        Assert.Contains(voices, x => x.Id == Settings.Provider!.VoiceId && x.Language == "zh");
        Assert.False(provider.Info.Capabilities.Streaming);
        Assert.False(provider.Info.Capabilities.VoiceAssets);
        Assert.False(typeof(IStreamingTtsEngine).IsAssignableFrom(provider.GetType()));
    }

    /// <summary>普通 JSON 请求不含参考音频、cookie、token 或伪装授权。</summary>
    [Fact]
    public async Task RequestUsesOnlyTextAndPresetAndRejectsInvalidMp3()
    {
        using var provider = Create(async (message, token) =>
        {
            Assert.Equal(HuoshanTtsProvider.Endpoint, message.RequestUri);
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Empty(message.Headers);
            using var json = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(token));
            Assert.Equal(2, System.Linq.Enumerable.Count(json.RootElement.EnumerateObject()));
            Assert.Equal(Request.Text, json.RootElement.GetProperty("text").GetString());
            Assert.Equal(Settings.Provider!.VoiceId, json.RootElement.GetProperty("speaker").GetString());
            return Json("{\"audio\":{\"data\":\"AQID\"}}");
        });
        var failure = await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(Settings, Request, null, default));
        Assert.Equal(VoiceErrorCode.InvalidAudio, failure.Code);
        Assert.False(failure.BackendUnknown);
    }

    /// <summary>服务错误、非 JSON、空音频和无效 base64 不能进入播放器。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"audio\":{\"data\":\"%%%\"}}")]
    [InlineData("{\"audio\":{\"data\":\"\"}}")]
    public async Task InvalidResponsesAreDiagnosedWithoutUnknown(string body)
    {
        using var provider = Create((_, _) => Task.FromResult(Json(body)));
        var error = await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(Settings, Request, null, default));
        Assert.Equal(VoiceErrorCode.InvalidAudio, error.Code);
        Assert.False(error.BackendUnknown);
    }

    /// <summary>非成功响应不打印服务返回的潜在敏感内容。</summary>
    [Fact]
    public async Task HttpRejectionIsActionableAndDoesNotLeakBody()
    {
        using var provider = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            { Content = new StringContent("sensitive-token") }));
        var error = await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(Settings, Request, null, default));
        Assert.Equal(VoiceErrorCode.SynthesisRejected, error.Code);
        Assert.Contains("429", error.Message);
        Assert.DoesNotContain("sensitive-token", error.Message);
    }

    /// <summary>过大 Content-Length 在解析前被拒绝。</summary>
    [Fact]
    public async Task OversizedResponseIsRejectedBeforeReading()
    {
        using var provider = Create((_, _) =>
        {
            var response = Json("{}");
            response.Content.Headers.ContentLength = 25 * 1024 * 1024;
            return Task.FromResult(response);
        });
        var error = await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(Settings, Request, null, default));
        Assert.Equal(VoiceErrorCode.InvalidAudio, error.Code);
    }

    /// <summary>取消必须传递到 HTTP；同一个实例后续仍可发请求。</summary>
    [Fact]
    public async Task CancelledRequestDoesNotPoisonNextRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var provider = Create(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        using var cancellation = new CancellationTokenSource();
        var task = provider.SynthesizeAsync(Settings, Request, null, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        var error = await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(Settings, Request, null, default));
        Assert.Equal(VoiceErrorCode.SynthesisRejected, error.Code);
        Assert.Equal(2, calls);
    }

    /// <summary>普通在线请求超时不能锁死 GPT 后端状态。</summary>
    [Fact]
    public async Task TimeoutIsRecoverableAndNotBackendUnknown()
    {
        using var provider = Create(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Json("{}"); });
        var error = await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(Settings, Request, null, default));
        Assert.Equal(VoiceErrorCode.Timeout, error.Code);
        Assert.False(error.BackendUnknown);
    }

    /// <summary>明确 WAV 编码可以正规化，无效 MP3 不能被误当 WAV。</summary>
    [Fact]
    public void AudioEncodingIsExplicitAndBounded()
    {
        var decoder = new TtsAudioDecoder();
        var wav = WavAndReferenceTests.Wave(32000, [100, -100]);
        Assert.Equal(32000, decoder.Decode(wav, TtsAudioEncoding.Pcm16Wav, default).SampleRate);
        Assert.Throws<VoiceException>(() => decoder.Decode(wav, TtsAudioEncoding.Mp3, default));
        Assert.Throws<VoiceException>(() => decoder.Decode(new byte[TtsAudioDecoder.MaxEncodedBytes + 1], TtsAudioEncoding.Mp3, default));
    }

    /// <summary>构建 JSON 响应，测试不依赖外部服务。</summary>
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    /// <summary>注入传输边界，业务协议仍执行生产代码。</summary>
    private static HuoshanTtsProvider Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(_ => new HttpClient(new Handler(send)));
    /// <summary>仅替代网络 I/O。</summary>
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        /// <summary>向生产 Provider 传回受控响应。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token) => send(message, token);
    }
}
