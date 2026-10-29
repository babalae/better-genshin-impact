using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>真实引擎与受控HTTP流验证，不调用用户的GPT-SoVITS。</summary>
public sealed class StreamingTransportTests
{
    /// <summary>HTTP还未结束时已经移交可读PCM，绝不等待整段再假流式。</summary>
    [Fact]
    public async Task ReadyPrecedesEofAndPlaybackLeaseOutlivesHttp()
    {
        using var fixture = new RoutingFixture(); using var body = new GateBody(PcmWave());
        using var engine = Engine((_, _) => Task.FromResult(Audio(body)));
        var ready = ContinuousVoiceFixture.NewCompletion<StreamingPcmAudio>();
        var synthesis = engine.SynthesizeStreamingAsync(Settings(fixture), Request(fixture), source =>
        { source.Retain(); ready.TrySetResult(source); }, null, default);
        using var audio = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await body.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(synthesis.IsCompleted);
        using var reader = audio.CreateReader();
        var bytes = new byte[2]; Assert.Equal(2, reader.Read(bytes, 0, 2));
        Assert.NotEqual(0, bytes[0]);
        body.Release.TrySetResult(false);
        var result = await synthesis;
        Assert.Equal(32000, result.SampleRate);
        Assert.Equal(32000, audio.PcmBytes);
        Assert.Equal(2, reader.Read(bytes, 0, 2));
    }

    /// <summary>只有文件头不能切断旧句；正常EOF后的无音频是可恢复的格式错误。</summary>
    [Fact]
    public async Task HeaderOnlyNeverPublishesReadyAndDoesNotPoisonBackend()
    {
        using var fixture = new RoutingFixture(); var posts = 0; var calls = 0;
        using var engine = Engine((_, _) =>
        {
            calls++; return Task.FromResult(Audio(new MemoryStream(++posts == 1 ? EmptyHeader() : PcmWave())));
        });
        var readyCount = 0;
        var error = await Assert.ThrowsAsync<VoiceException>(() => engine.SynthesizeStreamingAsync(Settings(fixture),
            Request(fixture), _ => readyCount++, null, default));
        Assert.Equal(VoiceErrorCode.InvalidAudio, error.Code); Assert.False(error.BackendUnknown); Assert.Equal(0, readyCount);
        Assert.NotEmpty((await engine.SynthesizeAsync(Settings(fixture), Request(fixture), null, default)).Wav);
        Assert.Equal(2, calls);
    }

    /// <summary>取消未收到PCM的请求也要等EOF，下一个请求不能先切权。</summary>
    [Fact]
    public async Task CancellationDrainsIssuedHttpBeforeNextWeightsAndDoesNotPublishLateReady()
    {
        using var fixture = new RoutingFixture(); using var body = new GateBody(EmptyHeader(), PcmWave()[44..]);
        var requests = 0; var posts = 0;
        using var engine = new GptSovitsEngine(_ => new HttpClient(new RoutingTransportTests.Handler((message, _) =>
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(message.Method == HttpMethod.Get ? RoutingTransportTests.Success()
                : Interlocked.Increment(ref posts) == 1 ? Audio(body) : Audio(new MemoryStream(PcmWave())));
        })), new ReferenceAudioSelector(), new WavProcessor());
        using var cancel = new CancellationTokenSource(); var ready = 0;
        var first = engine.SynthesizeStreamingAsync(Settings(fixture), Request(fixture), _ => ready++, null, cancel.Token);
        await body.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancel.Cancel();
        var second = engine.SynthesizeAsync(Settings(fixture), Request(fixture), null, default);
        Assert.Equal(3, requests); Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        Assert.False(body.ReadToken.IsCancellationRequested);
        body.Release.TrySetResult(false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.NotEmpty((await second).Wav); Assert.Equal(6, requests); Assert.Equal(0, ready);
    }

    /// <summary>已播放的源不使用候选取消令牌，仍能接收剩余数据供旧语音继续。</summary>
    [Fact]
    public async Task CancellationAfterReadyKeepsExistingSourceUsableUntilEof()
    {
        using var fixture = new RoutingFixture(); var wave = PcmWave();
        using var body = new GateBody(wave[..16044], wave[16044..]);
        using var engine = Engine((_, _) => Task.FromResult(Audio(body)));
        using var cancel = new CancellationTokenSource();
        var ready = ContinuousVoiceFixture.NewCompletion<StreamingPcmAudio>();
        var task = engine.SynthesizeStreamingAsync(Settings(fixture), Request(fixture), source =>
        { source.Retain(); ready.TrySetResult(source); }, null, cancel.Token);
        using var audio = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await body.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancel.Cancel();
        Assert.False(body.ReadToken.IsCancellationRequested); body.Release.TrySetResult(false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        using var reader = audio.CreateReader();
        var received = new byte[32000]; Assert.Equal(received.Length, reader.Read(received, 0, received.Length));
        Assert.Equal(wave[44..], received);
    }

    /// <summary>中途传输失败保持后端未知；后来的Read=0不能作为恢复证明。</summary>
    [Fact]
    public async Task InterruptedStreamPoisonsBackendAndFailsPublishedSource()
    {
        using var fixture = new RoutingFixture(); using var body = new GateBody(PcmWave());
        using var engine = Engine((_, _) => Task.FromResult(Audio(body)));
        var ready = ContinuousVoiceFixture.NewCompletion<StreamingPcmAudio>();
        var task = engine.SynthesizeStreamingAsync(Settings(fixture), Request(fixture), source =>
        { source.Retain(); ready.TrySetResult(source); }, null, default);
        using var audio = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        body.Release.TrySetResult(true);
        var error = await Assert.ThrowsAsync<VoiceException>(() => task);
        Assert.True(error.BackendUnknown); Assert.Equal(VoiceErrorCode.InterruptedResponse, error.Code);
        using var reader = audio.CreateReader(); Assert.Throws<VoiceException>(() => reader.Read(new byte[2], 0, 2));
        var blocked = await Assert.ThrowsAsync<VoiceException>(() => engine.SynthesizeAsync(Settings(fixture), Request(fixture), null, default));
        Assert.Equal(VoiceErrorCode.BackendUnknown, blocked.Code);
    }

    /// <summary>跨网络分块的头和不足采样帧不能丢失PCM。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4095)]
    public async Task ArbitraryReadBoundariesPreserveExactPcm(int blockSize)
    {
        using var fixture = new RoutingFixture(); var wave = PcmWave();
        using var engine = Engine((_, _) => Task.FromResult(Audio(new FragmentedBody(wave, blockSize))));
        var result = await engine.SynthesizeStreamingAsync(Settings(fixture), Request(fixture), _ => { }, null, default);
        Assert.Equal(new WavProcessor().ValidateCompletedResponse(wave).Bytes, result.Wav);
    }

    /// <summary>重复RIFF头不是音频；检测到后仍排空到正常EOF。</summary>
    [Fact]
    public async Task RepeatedHeaderIsRejectedWithoutPlayingHeaderBytes()
    {
        using var fixture = new RoutingFixture(); var wave = PcmWave();
        using var engine = Engine((_, _) => Task.FromResult(Audio(new FragmentedBody(wave.Concat(wave).ToArray(), 31))));
        var error = await Assert.ThrowsAsync<VoiceException>(() => engine.SynthesizeStreamingAsync(Settings(fixture), Request(fixture), _ => { }, null, default));
        Assert.Equal(VoiceErrorCode.InvalidAudio, error.Code); Assert.False(error.BackendUnknown);
    }

    /// <summary>所有测试使用现有小型资产夹具与隔离输出目录。</summary>
    internal static AppSettings Settings(RoutingFixture fixture) => new(fixture.Root, new("http://localhost:19001/"),
        new(1, 3, 20), fixture.LegacyVoice, new(fixture.Root, fixture.Root), new(0, 0));
    /// <summary>请求仅引用夹具资产。</summary>
    internal static TtsRequest Request(RoutingFixture fixture) => new("测试对白", new("测试声线",
        fixture.LegacyVoice.GptWeightsPath, fixture.LegacyVoice.SovitsWeightsPath,
        [new(fixture.LegacyVoice.ReferenceAudioPath, fixture.LegacyVoice.PromptText)]));
    /// <summary>制造有非零样本的半秒PCM。</summary>
    internal static byte[] PcmWave() => WavAndReferenceTests.Wave(32000,
        Enumerable.Range(0, 16000).Select(i => (short)(i % 2 == 0 ? 300 : -300)).ToArray());
    /// <summary>本地流式协议的空data长度头。</summary>
    internal static byte[] EmptyHeader() => WavAndReferenceTests.Wave(32000, []);
    /// <summary>权重切换返回success，只有tts进入受控响应。</summary>
    internal static GptSovitsEngine Engine(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(_ => new HttpClient(new RoutingTransportTests.Handler((message, token) => message.Method == HttpMethod.Get
            ? Task.FromResult(RoutingTransportTests.Success()) : handler(message, token))), new ReferenceAudioSelector(), new WavProcessor());
    /// <summary>模拟音频HTTP响应。</summary>
    internal static HttpResponseMessage Audio(Stream stream)
    {
        var content = new StreamContent(stream); content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        return new(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>暂停在首段之后，验证Ready/取消/下一次请求的所有权。</summary>
    internal sealed class GateBody(byte[] initial, byte[]? remainder = null) : MemoryStream(initial)
    {
        /// <summary>已发请求正在等待下一个网络块。</summary>
        public TaskCompletionSource<bool> Waiting { get; } = ContinuousVoiceFixture.NewCompletion<bool>();
        /// <summary>true模拟断流，false正常发送余下PCM及EOF。</summary>
        public TaskCompletionSource<bool> Release { get; } = ContinuousVoiceFixture.NewCompletion<bool>();
        /// <summary>已发HTTP的读取令牌，用于确认候选取消不会断开连接。</summary>
        public CancellationToken ReadToken { get; private set; }
        /// <summary>余下PCM读取位置。</summary>
        private int _remaining;
        /// <summary>提供真实异步分段网络读取。</summary>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < Length) return await base.ReadAsync(buffer, cancellationToken);
            ReadToken = cancellationToken; Waiting.TrySetResult(true);
            if (await Release.Task.WaitAsync(cancellationToken)) throw new IOException("Controlled midstream failure");
            var bytes = remainder ?? []; var count = Math.Min(buffer.Length, bytes.Length - _remaining);
            bytes.AsMemory(_remaining, count).CopyTo(buffer); _remaining += count; return count;
        }
    }

    /// <summary>网络Read边界与WAV头/PCM帧完全无关。</summary>
    private sealed class FragmentedBody(byte[] bytes, int blockSize) : MemoryStream(bytes)
    {
        /// <summary>将每次异步读取限制到指定大小。</summary>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, blockSize)], cancellationToken);
    }
}
