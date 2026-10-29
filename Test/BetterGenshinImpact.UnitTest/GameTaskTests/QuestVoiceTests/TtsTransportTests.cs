using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：TtsTransportTests。</summary>
public sealed class TtsTransportTests : IDisposable
{
    /// <summary>移植回归验证： directory。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuestVoiceTtsTests", Guid.NewGuid().ToString("N"));
    /// <summary>当前实例采用的配置快照。</summary>
    private readonly AppSettings _settings;
    /// <summary>移植回归验证： request。</summary>
    private readonly TtsRequest _request;

    /// <summary>移植回归验证：TtsTransportTests。</summary>
    public TtsTransportTests()
    {
        Directory.CreateDirectory(_directory);
        var model = Path.Combine(_directory, "甘雨 & model.ckpt");
        File.WriteAllBytes(model, [1]);
        var reference = Path.Combine(_directory, "reference.wav");
        File.WriteAllBytes(reference, WavAndReferenceTests.Wave(48000, [100, -100]));
        _settings = new(_directory, new Uri("http://localhost:19001/"), new(1, 2, 2),
            new("甘雨", model, model, reference, "你好。"), new(_directory, _directory), new(900, 800));
        _request = new("你好，旅行者。", new("甘雨", model, model, [new(reference, "你好。") ]));
    }

    /// <summary>移植回归验证：Real protocol encodes paths reapplies weights and posts configured json。</summary>
    [Fact]
    public async Task Real_protocol_encodes_paths_reapplies_weights_and_posts_configured_json()
    {
        var paths = new List<string>();
        var payloads = new List<JsonElement>();
        using var engine = Engine(async (request, token) =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            if (request.Method == HttpMethod.Get) return Success();
            payloads.Add(JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(token)));
            return Audio();
        });
        var first = await engine.SynthesizeAsync(_settings, _request, null, default);
        await engine.SynthesizeAsync(_settings, _request, null, default);
        Assert.Equal(6, paths.Count);
        Assert.Equal("/set_gpt_weights?weights_path=" + Uri.EscapeDataString(_request.Voice.GptWeightsPath), paths[0]);
        Assert.StartsWith("/set_sovits_weights?weights_path=", paths[1]);
        Assert.Equal(paths[0], paths[3]);
        Assert.Equal("/tts", paths[2]);
        var body = payloads[0];
        Assert.Equal(_request.Text, body.GetProperty("text").GetString());
        Assert.Equal("zh", body.GetProperty("text_lang").GetString());
        Assert.Equal("zh", body.GetProperty("prompt_lang").GetString());
        Assert.Equal("你好。", body.GetProperty("prompt_text").GetString());
        Assert.Equal(_request.Voice.References[0].Path, body.GetProperty("ref_audio_path").GetString());
        Assert.Equal("wav", body.GetProperty("media_type").GetString());
        Assert.Equal("cut5", body.GetProperty("text_split_method").GetString());
        Assert.True(body.GetProperty("streaming_mode").GetBoolean());
        Assert.Equal(32000, first.SampleRate);
        Assert.True(first.SynthesisSeconds >= first.TimeToFirstByteSeconds);
    }

    /// <summary>移植回归验证：Cancelled response keeps ownership until eof and discards old result。</summary>
    [Fact]
    public async Task Cancelled_response_keeps_ownership_until_eof_and_discards_old_result()
    {
        var stream = new HeldStream(WavAndReferenceTests.Wave(32000, [300, -300]));
        var calls = 0;
        var posts = 0;
        using var engine = Engine((request, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(request.Method == HttpMethod.Get ? Success()
                : Interlocked.Increment(ref posts) == 1 ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) } : Audio());
        });
        using var cancelled = new CancellationTokenSource();
        var phases = new List<TtsPhase>();
        var first = engine.SynthesizeAsync(_settings, _request, new InlineProgress<TtsPhase>(phases.Add), cancelled.Token);
        await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancelled.Cancel();
        Assert.Contains(TtsPhase.Draining, phases);
        var second = engine.SynthesizeAsync(_settings, _request, null, default);
        await Task.Delay(50);
        Assert.Equal(3, calls);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        stream.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.NotEmpty((await second).Wav);
        Assert.Equal(6, calls);
    }

    /// <summary>移植回归验证：Header then disconnect never repairs partial audio and blocks reentry。</summary>
    [Fact]
    public async Task Header_then_disconnect_never_repairs_partial_audio_and_blocks_reentry()
    {
        using var engine = Engine((request, _) => Task.FromResult(request.Method == HttpMethod.Get ? Success()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream(WavAndReferenceTests.Wave(32000, [20, -20]))) }));
        var error = await Assert.ThrowsAsync<VoiceException>(() => engine.SynthesizeAsync(_settings, _request, null, default));
        Assert.Equal(VoiceErrorCode.InterruptedResponse, error.Code);
        Assert.True(error.BackendUnknown);
        error = await Assert.ThrowsAsync<VoiceException>(() => engine.SynthesizeAsync(_settings, _request, null, default));
        Assert.Equal(VoiceErrorCode.BackendUnknown, error.Code);
    }

    /// <summary>移植回归验证：Failures keep their cause and uncertainty。</summary>
    [Theory]
    [InlineData("refused", VoiceErrorCode.Unreachable, false)]
    [InlineData("weight", VoiceErrorCode.WeightRejected, false)]
    [InlineData("tts", VoiceErrorCode.SynthesisRejected, false)]
    [InlineData("silent", VoiceErrorCode.InvalidAudio, false)]
    [InlineData("timeout", VoiceErrorCode.Timeout, true)]
    public async Task Failures_keep_their_cause_and_uncertainty(string kind, VoiceErrorCode code, bool unknown)
    {
        using var engine = Engine(async (request, token) =>
        {
            if (kind == "refused") throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused));
            if (kind == "weight") return new(HttpStatusCode.BadRequest);
            if (request.Method == HttpMethod.Get) return Success();
            if (kind == "tts") return new(HttpStatusCode.BadRequest);
            if (kind == "timeout") await Task.Delay(Timeout.Infinite, token);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(WavAndReferenceTests.Wave(16000, [0, 0])) };
        });
        var error = await Assert.ThrowsAsync<VoiceException>(() => engine.SynthesizeAsync(_settings, _request, null, default));
        Assert.Equal(code, error.Code);
        Assert.Equal(unknown, error.BackendUnknown);
    }

    /// <summary>移植回归验证：Empty input is client configuration error without network。</summary>
    [Fact]
    public async Task Empty_input_is_client_configuration_error_without_network()
    {
        using var engine = Engine((_, _) => throw new InvalidOperationException("Must not access network"));
        var error = await Assert.ThrowsAsync<VoiceException>(() => engine.SynthesizeAsync(_settings, _request with { Text = " " }, null, default));
        Assert.Equal(VoiceErrorCode.Configuration, error.Code);
    }

    /// <summary>移植回归验证：Malformed weight acknowledgement is a backend rejection。</summary>
    [Fact]
    public async Task Malformed_weight_acknowledgement_is_a_backend_rejection()
    {
        using var engine = Engine((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"message\":42}") }));
        var error = await Assert.ThrowsAsync<VoiceException>(() => engine.SynthesizeAsync(_settings, _request, null, default));
        Assert.Equal(VoiceErrorCode.WeightRejected, error.Code);
    }

    /// <summary>移植回归验证：Cancelled body is discarded before audio validation after normal eof。</summary>
    [Fact]
    public async Task Cancelled_body_is_discarded_before_audio_validation_after_normal_eof()
    {
        var stream = new HeldStream(WavAndReferenceTests.Wave(16000, [0, 0]));
        using var engine = Engine((request, _) => Task.FromResult(request.Method == HttpMethod.Get ? Success()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        using var cancelled = new CancellationTokenSource();
        var operation = engine.SynthesizeAsync(_settings, _request, null, cancelled.Token);
        await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancelled.Cancel();
        stream.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    /// <summary>移植回归验证：Shutdown is local cancellation with unknown remote state not an elapsed timeout。</summary>
    [Fact]
    public async Task Shutdown_is_local_cancellation_with_unknown_remote_state_not_an_elapsed_timeout()
    {
        var stream = new HeldStream(WavAndReferenceTests.Wave(32000, [300, -300]));
        using var engine = Engine((request, _) => Task.FromResult(request.Method == HttpMethod.Get ? Success()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        var operation = engine.SynthesizeAsync(_settings, _request, null, default);
        await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        engine.Shutdown();
        var error = await Assert.ThrowsAsync<VoiceException>(() => operation);
        Assert.Equal(VoiceErrorCode.Cancelled, error.Code);
        Assert.True(error.BackendUnknown);
    }

    /// <summary>移植回归验证：Engine。</summary>
    private GptSovitsEngine Engine(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => new(_ => new HttpClient(new Handler(handler)), new ReferenceAudioSelector(), new WavProcessor());
    /// <summary>移植回归验证：Success。</summary>
    private static HttpResponseMessage Success() => new(HttpStatusCode.OK) { Content = new StringContent("{\"message\":\"success\"}") };
    /// <summary>移植回归验证：Audio。</summary>
    private static HttpResponseMessage Audio() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(WavAndReferenceTests.Wave(32000, [200, -200])) };
    /// <summary>释放本实例拥有的本地资源。</summary>
    public void Dispose() => Directory.Delete(_directory, true);

    /// <summary>移植回归验证：Handler。</summary>
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        /// <summary>移植回归验证：SendAsync。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
    /// <summary>移植回归验证：HeldStream。</summary>
    private sealed class HeldStream(byte[] bytes) : MemoryStream(bytes)
    {
        /// <summary>移植回归验证：Waiting。</summary>
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>移植回归验证：Release。</summary>
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>异步读取并校验 OCR 执行诊断或图像快照。</summary>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= Length)
            {
                Waiting.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
    /// <summary>移植回归验证：BrokenStream。</summary>
    private sealed class BrokenStream(byte[] bytes) : MemoryStream(bytes)
    {
        /// <summary>异步读取并校验 OCR 执行诊断或图像快照。</summary>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Position >= Length ? ValueTask.FromException<int>(new IOException("connection closed mid-body")) : base.ReadAsync(buffer, cancellationToken);
    }
    /// <summary>移植回归验证：InlineProgress。</summary>
    internal sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        /// <summary>移植回归验证：Report。</summary>
        public void Report(T value) => action(value);
    }
}
