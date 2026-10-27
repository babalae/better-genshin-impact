using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>生产HTTP适配器真实协议/音频契约测试；隔离响应不代表模型推理验收。</summary>
public sealed class HttpSpeechProviderTests
{
    /// <summary>不包含GPT文件的请求。</summary>
    private static TtsRequest Request => new("你好，A&B？", new("普通声线", "", "", []));
    /// <summary>反向代理子路径和独立参数。</summary>
    private static AppSettings Settings(string id) => new(".", new("https://service.test/proxy/"), new(1, 1, 4),
        new("普通", "", "", "", ""), new(".", "."), new(0, 0), Provider: new(id, "remote", id switch
        { "voicevox" => "4", "vits-simple-api" => "BERT-VITS2:7", "google-translate" => "zh-CN", _ => "custom" }),
        Extra: new(Language: "zh", Model: "custom-model", ApiKey: "test-secret", Rate: 1.25, Pitch: .05));
    /// <summary>有界非静音PCM16样本，协议测试不依赖私有音频。</summary>
    private static byte[] Wav()
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8); writer.Write(52); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(24000); writer.Write(48000);
        writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(16);
        for (var i = 0; i < 8; i++) writer.Write((short)(i * 100));
        return memory.ToArray();
    }
    /// <summary>HTTP响应由每次调用拥有并释放。</summary>
    private static HttpResponseMessage Audio() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Wav()) };
    /// <summary>JSON无需复制服务端实现。</summary>
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    /// <summary>唯一的隔离网络边界，其余均为生产组件。</summary>
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        /// <summary>观察实际发出的请求。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    /// <summary>用生产派生类执行同一组生命周期契约。</summary>
    private static HttpSpeechProvider Create(string id, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        HttpClient Client(TimeoutSettings _) => new(new Handler(send));
        return id switch { "voicevox" => new VoicevoxTtsProvider(Client), "vits-simple-api" => new VitsSimpleApiTtsProvider(Client),
            "google-translate" => new GoogleTranslateTtsProvider(Client), _ => new OpenAiSpeechTtsProvider(Client) };
    }

    /// <summary>VOICEVOX保留查询的accent数据，并以style ID合成，不误用speaker UUID。</summary>
    [Fact]
    public async Task VoicevoxQueriesThenSynthesizesWithSelectedStyle()
    {
        var calls = 0;
        using var provider = Create("voicevox", async (message, ct) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.StartsWith("https://service.test/proxy/", message.RequestUri!.AbsoluteUri);
            if (++calls == 1)
            {
                Assert.Contains("audio_query?speaker=4&text=", message.RequestUri.AbsoluteUri);
                Assert.Contains("A%26B", message.RequestUri.AbsoluteUri);
                return Json("{\"accent_phrases\":[{\"marker\":42}],\"speedScale\":1}");
            }
            Assert.EndsWith("synthesis?speaker=4", message.RequestUri.AbsoluteUri);
            using var json = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
            Assert.Equal(42, json.RootElement.GetProperty("accent_phrases")[0].GetProperty("marker").GetInt32());
            Assert.Equal(1.25, json.RootElement.GetProperty("speedScale").GetDouble());
            Assert.Equal(.05, json.RootElement.GetProperty("pitchScale").GetDouble());
            return Audio();
        });
        var result = await provider.SynthesizeAsync(Settings("voicevox"), Request, null, default);
        Assert.Equal(2, calls); Assert.Equal(Wav(), result.Wav); Assert.Equal(24000, result.SampleRate);
    }

    /// <summary>声音列表只包含可合成的talk styles。</summary>
    [Fact]
    public async Task VoicevoxEnumeratesTalkStyles()
    {
        using var provider = Create("voicevox", (_, _) => Task.FromResult(Json("""
            [{"name":"話者","styles":[{"id":1,"name":"普通"},{"id":2,"name":"歌","type":"sing"}]}]
            """)));
        var voices = await provider.GetVoicesAsync(Settings("voicevox"), default);
        Assert.Equal("1", Assert.Single(voices).Id);
        Assert.Contains("話者", voices[0].DisplayName);
    }

    /// <summary>模型类型保留在profile声音中，POST JSON和X-API-KEY满足当前服务接口。</summary>
    [Fact]
    public async Task VitsSendsModelIdLanguageAndInverseRateWithHeaderKey()
    {
        using var provider = Create("vits-simple-api", async (message, ct) =>
        {
            Assert.Equal("https://service.test/proxy/voice/bert-vits2", message.RequestUri!.AbsoluteUri);
            Assert.Equal("test-secret", Assert.Single(message.Headers.GetValues("X-API-KEY")));
            Assert.Equal("application/json", message.Content!.Headers.ContentType!.ToString());
            using var json = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
            Assert.Equal(7, json.RootElement.GetProperty("id").GetInt32());
            Assert.Equal("zh", json.RootElement.GetProperty("lang").GetString());
            Assert.Equal(.8, json.RootElement.GetProperty("length").GetDouble());
            Assert.False(json.RootElement.GetProperty("streaming").GetBoolean());
            Assert.Equal("wav", json.RootElement.GetProperty("format").GetString());
            return Audio();
        });
        Assert.Equal(Wav(), (await provider.SynthesizeAsync(Settings("vits-simple-api"), Request, null, default)).Wav);
    }

    /// <summary>不把未支持的转换/特殊情感模型发布为可用voice。</summary>
    [Fact]
    public async Task VitsEnumerationKeepsModelTypesDistinct()
    {
        using var provider = Create("vits-simple-api", (_, _) => Task.FromResult(Json("""
            {"VITS":[{"id":0,"name":"A","lang":["zh"]}],"BERT-VITS2":[{"id":0,"name":"B","lang":["ja","en"]}],"HUBERT-VITS":[]}
            """)));
        var voices = await provider.GetVoicesAsync(Settings("vits-simple-api"), default);
        Assert.Equal(new[] { "VITS:0", "BERT-VITS2:0" }, voices.Select(x => x.Id));
    }

    /// <summary>自定义声音、可选认证及明确格式，不上传GPT资产。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("test-secret")]
    public async Task OpenAiSpeechSendsOptionalAuthentication(string key)
    {
        using var provider = Create("openai-speech", async (message, ct) =>
        {
            Assert.Equal("https://service.test/proxy/audio/speech", message.RequestUri!.AbsoluteUri);
            Assert.Equal(string.IsNullOrEmpty(key) ? null : "Bearer " + key, message.Headers.Authorization?.ToString());
            using var json = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
            Assert.Equal("custom", json.RootElement.GetProperty("voice").GetString());
            Assert.Equal("custom-model", json.RootElement.GetProperty("model").GetString());
            Assert.Equal(Request.Text, json.RootElement.GetProperty("input").GetString());
            Assert.Equal("wav", json.RootElement.GetProperty("response_format").GetString());
            return Audio();
        });
        var settings = Settings("openai-speech");
        await provider.SynthesizeAsync(settings with { Extra = settings.Extra! with { ApiKey = key } }, Request, null, default);
        Assert.DoesNotContain("test-secret", settings.Extra!.ToString());
    }

    /// <summary>原始PCM必须按官方24k单声道协议补WAV，奇数响应拒绝。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAiPcmIsWrappedOrRejected(bool odd)
    {
        using var provider = Create("openai-speech", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(odd ? new byte[] { 1, 2, 3 } : new byte[] { 1, 2, 3, 4 }) }));
        var settings = Settings("openai-speech");
        settings = settings with { Extra = settings.Extra! with { Format = "pcm" } };
        if (odd) Assert.Equal(VoiceErrorCode.InvalidAudio, (await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(settings, Request, null, default))).Code);
        else
        {
            var result = await provider.SynthesizeAsync(settings, Request, null, default);
            Assert.Equal(24000, result.SampleRate);
            Assert.Equal("Pcm16", result.SourceEncoding.ToString());
        }
    }

    /// <summary>各引擎的失败/取消不污染下一次请求，也不进入GPT Unknown。</summary>
    [Theory]
    [InlineData("voicevox")]
    [InlineData("vits-simple-api")]
    [InlineData("openai-speech")]
    public async Task CancelThenRejectThenRecover(string id)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var provider = Create(id, async (message, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            if (calls == 2) return new(HttpStatusCode.Unauthorized) { Content = new StringContent("test-secret private-error") };
            return message.RequestUri!.AbsolutePath.EndsWith("audio_query") ? Json("{\"accent_phrases\":[]}") : Audio();
        });
        var settings = Settings(id);
        using var cancellation = new CancellationTokenSource();
        var pending = provider.SynthesizeAsync(settings, Request, null, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var rejected = await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(settings, Request, null, default));
        Assert.False(rejected.BackendUnknown); Assert.Contains("401", rejected.Message); Assert.DoesNotContain("test-secret", rejected.ToString());
        Assert.Equal(Wav(), (await provider.SynthesizeAsync(settings, Request, null, default)).Wav);
    }

    /// <summary>单请求超时必须短于更大的合成总期限。</summary>
    [Fact]
    public async Task RequestTimeoutAppliesWithinSynthesisDeadline()
    {
        using var provider = Create("openai-speech", async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Audio(); });
        var task = provider.SynthesizeAsync(Settings("openai-speech"), Request, null, default);
        var completed = await Task.WhenAny(task, Task.Delay(2500));
        Assert.Same(task, completed);
        Assert.Equal(VoiceErrorCode.Timeout, (await Assert.ThrowsAsync<VoiceException>(() => task)).Code);
    }

    /// <summary>格式错误与超大响应在播放器之前被拦截。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrOversizedAudioIsRejected(bool oversized)
    {
        using var provider = Create("openai-speech", (_, _) =>
        {
            var response = Json("not audio");
            if (oversized) response.Content.Headers.ContentLength = TtsAudioDecoder.MaxEncodedBytes + 1;
            return Task.FromResult(response);
        });
        Assert.Equal(VoiceErrorCode.InvalidAudio, (await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(Settings("openai-speech"), Request, null, default))).Code);
    }

    /// <summary>Google分段保留完整文本，包含非BMP字符且不拆代理对。</summary>
    [Fact]
    public void GoogleSplitsWithoutLosingUnicodeOrPunctuation()
    {
        var text = string.Concat(Enumerable.Repeat("你好😀。 ", 50));
        var parts = GoogleTranslateTtsProvider.SplitText(text);
        Assert.Equal(text, string.Concat(parts));
        Assert.All(parts, x => { Assert.InRange(x.Length, 1, 100); Assert.False(char.IsLowSurrogate(x[0])); Assert.False(char.IsHighSurrogate(x[^1])); });
    }

    /// <summary>Google不能把空白或标点分段当成必须返回语音的请求。</summary>
    [Fact]
    public async Task GoogleDoesNotSendUnspeakableSegments()
    {
        using var provider = Create("google-translate", async (message, token) =>
        {
            var encoded = await message.Content!.ReadAsStringAsync(token);
            using var rpc = JsonDocument.Parse(Uri.UnescapeDataString(encoded[6..].Replace("+", " ")));
            using var parameters = JsonDocument.Parse(rpc.RootElement[0][0][1].GetString()!);
            Assert.Equal("你好", parameters.RootElement[0].GetString());
            return new(HttpStatusCode.Forbidden);
        });
        var settings = Settings("google-translate") with { Extra = new(Rate: 1) };
        var error = await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(settings,
            Request with { Text = new string(' ', 100) + "你好" }, null, default));
        Assert.Equal(VoiceErrorCode.SynthesisRejected, error.Code);
    }

    /// <summary>官方旧模型的建议列表不能提供仅新模型支持的声音，兼容服务仍可手输。</summary>
    [Fact]
    public async Task OpenAiKnownModelRecommendationsMatchItsVoices()
    {
        using var provider = Create("openai-speech", (_, _) => throw new Exception("建议列表无需网络"));
        var settings = Settings("openai-speech") with { Extra = new(Model: "tts-1") };
        var voices = await provider.GetVoicesAsync(settings, default);
        Assert.Equal(9, voices.Count);
        Assert.DoesNotContain(voices, x => x.Id == "marin" || x.Id == "cedar" || x.Id == "ballad" || x.Id == "verse");
    }

    /// <summary>RPC成功但无音频必须报告格式错误，而非泄露未处理的数组异常。</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[\"invalid-base64\"]")]
    public async Task GoogleMissingAudioIsInvalidAudio(string embedded)
    {
        using var provider = Create("google-translate", (_, _) => Task.FromResult(Json(
            JsonSerializer.Serialize(new object[] { new object[] { "wrb.fr", "jQ1olc", embedded } }))));
        var settings = Settings("google-translate") with { Extra = new(Rate: 1) };
        Assert.Equal(VoiceErrorCode.InvalidAudio, (await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(settings, Request, null, default))).Code);
    }

    /// <summary>完整音频耗时与首响应字节不是同一指标。</summary>
    [Fact]
    public async Task FirstResponseByteDoesNotIncludeTailDelay()
    {
        using var provider = Create("openai-speech", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new DelayedTailStream(Wav())) }));
        var result = await provider.SynthesizeAsync(Settings("openai-speech"), Request, null, default);
        Assert.True(result.SynthesisSeconds - result.TimeToFirstByteSeconds > .1);
    }

    /// <summary>头部已到而正文挂起时，RequestSeconds仍须约束接收。</summary>
    [Fact]
    public async Task RequestDeadlineIncludesBodyRead()
    {
        using var provider = Create("openai-speech", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new HangingBodyStream()) }));
        var pending = provider.SynthesizeAsync(Settings("openai-speech"), Request, null, default);
        Assert.Same(pending, await Task.WhenAny(pending, Task.Delay(2500)));
        Assert.Equal(VoiceErrorCode.Timeout, (await Assert.ThrowsAsync<VoiceException>(() => pending)).Code);
    }

    /// <summary>带Key的请求不能被第三方Location重定向到用户未配置的地址。</summary>
    [Fact]
    public async Task CredentialedProviderDoesNotFollowRedirects()
    {
        using var source = new TcpListener(IPAddress.Loopback, 0);
        using var destination = new TcpListener(IPAddress.Loopback, 0);
        source.Start(); destination.Start();
        var port = ((IPEndPoint)source.LocalEndpoint).Port;
        var destinationPort = ((IPEndPoint)destination.LocalEndpoint).Port;
        using var provider = TtsProviderRegistry.Create("vits-simple-api");
        var settings = Settings("vits-simple-api") with { TtsServiceUrl = new($"http://127.0.0.1:{port}/") };
        var redirected = destination.AcceptTcpClientAsync();
        var synthesis = provider.SynthesizeAsync(settings, Request, null, default);
        using var request = await source.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
        using var reader = new StreamReader(request.GetStream(), Encoding.ASCII, leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{destinationPort}/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await request.GetStream().WriteAsync(header);
        var error = await Assert.ThrowsAsync<VoiceException>(() => synthesis);
        try
        {
            Assert.Equal(VoiceErrorCode.SynthesisRejected, error.Code);
            Assert.Contains("302", error.Message);
            Assert.False(redirected.IsCompletedSuccessfully);
        }
        finally
        {
            destination.Stop();
            try { using var unexpected = await redirected; } catch (SocketException) { }
        }
    }

    /// <summary>末尾延迟用于观察完整接收和首字节时间差。</summary>
    private sealed class DelayedTailStream(byte[] bytes) : MemoryStream(bytes, false)
    {
        /// <summary>只在EOF之前延迟，不篡改样本。</summary>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position == Length) await Task.Delay(200, cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
    /// <summary>隔离模拟响应体迟迟不返回数据。</summary>
    private sealed class HangingBodyStream : MemoryStream
    {
        /// <summary>等待取消以验证正文的请求期限。</summary>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
    }
}
