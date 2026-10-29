using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousVoiceTransportTests。</summary>
public sealed class ContinuousVoiceTransportTests
{
    /// <summary>移植回归验证：Real engine drains stale HTTP body before next weights without stopping A。</summary>
    [Fact]
    public async Task Real_engine_drains_stale_HTTP_body_before_next_weights_without_stopping_A()
    {
        using var stream = new ControlledBody();
        var requests = new ConcurrentQueue<string>();
        var texts = new ConcurrentQueue<string>();
        using var engine = CreateEngine(async (request, token) =>
        {
            requests.Enqueue(request.RequestUri!.PathAndQuery);
            if (request.Method == HttpMethod.Get) return RoutingTransportTests.Success();
            var text = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)).RootElement.GetProperty("text").GetString()!;
            texts.Enqueue(text);
            return text == "B" ? new(HttpStatusCode.OK) { Content = new StreamContent(stream) } : RoutingTransportTests.Audio();
        });
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync(engine, new WavProcessor());
        f.Offer(1, "A"); var a = await f.Player.NextAsync();
        f.Offer(2, "B", "刻晴"); await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        f.Offer(3, "C", "甘雨");
        Assert.Equal(6, requests.Count); Assert.False(stream.ReadToken.IsCancellationRequested);
        Assert.False(a.Token.IsCancellationRequested); Assert.Single(f.Player.All);
        stream.Release.SetResult(false);
        await f.EventAsync(ContinuousVoiceEventKind.AudioReady, 3);
        await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "A", "B", "C" }, texts); Assert.Equal(9, requests.Count);
        Assert.Equal(2, f.Artifacts.All.Count); Assert.Single(f.Player.All);
        a.Exit.SetResult(true); var c = await f.Player.NextAsync(); c.Exit.SetResult(true);
        await f.Session.CompleteAsync();
        Assert.False(f.Session.Snapshot.BackendUnknown); Assert.Equal(1, f.Player.MaximumActive);
    }

    /// <summary>移植回归验证：Interrupted stale response locks session and backend without stopping A。</summary>
    [Fact]
    public async Task Interrupted_stale_response_locks_session_and_backend_without_stopping_A()
    {
        using var stream = new ControlledBody(); var requests = 0; var posts = 0;
        using var engine = CreateEngine((request, _) =>
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(request.Method == HttpMethod.Get ? RoutingTransportTests.Success()
                : Interlocked.Increment(ref posts) == 1 ? RoutingTransportTests.Audio()
                : new(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        });
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync(engine, new WavProcessor());
        f.Offer(1, "A"); var a = await f.Player.NextAsync();
        f.Offer(2, "B"); await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10)); f.Offer(3, "C");
        stream.Release.SetResult(true);
        await f.EventAsync(ContinuousVoiceEventKind.CandidateFailed, 2);
        Assert.True(f.Session.Snapshot.BackendUnknown); Assert.Equal(VoiceErrorCode.InterruptedResponse, f.Session.Snapshot.Error);
        f.Offer(4, "D"); Assert.Equal(6, requests); Assert.False(a.Token.IsCancellationRequested);
        a.Exit.SetResult(true); await f.Session.CompleteAsync(); Assert.Single(f.Player.All);
        var voice = f.Settings.Voice;
        var error = await Assert.ThrowsAsync<VoiceException>(() => engine.SynthesizeAsync(f.Settings,
            new("probe", new(voice.Name, voice.GptWeightsPath, voice.SovitsWeightsPath,
                [new(voice.ReferenceAudioPath, voice.PromptText)])), null, default));
        Assert.Equal(VoiceErrorCode.BackendUnknown, error.Code); Assert.Equal(6, requests);
    }

    /// <summary>移植回归验证：Dispose waits for issued real HTTP to drain without shutdown。</summary>
    [Fact]
    public async Task Dispose_waits_for_issued_real_HTTP_to_drain_without_shutdown()
    {
        using var stream = new ControlledBody();
        using var engine = CreateEngine((request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? RoutingTransportTests.Success() : new(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync(engine, new WavProcessor());
        f.Offer(1, "A"); await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var disposing = f.Session.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted); Assert.False(stream.ReadToken.IsCancellationRequested);
        stream.Release.SetResult(false); await disposing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(f.Session.Snapshot.BackendUnknown); Assert.Empty(f.Player.All); Assert.Empty(f.Artifacts.All);
    }

    /// <summary>移植回归验证：CreateEngine。</summary>
    private static GptSovitsEngine CreateEngine(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(_ => new HttpClient(new RoutingTransportTests.Handler(handler)), new ReferenceAudioSelector(), new WavProcessor());

    /// <summary>移植回归验证：ControlledBody。</summary>
    private sealed class ControlledBody() : MemoryStream(WavAndReferenceTests.Wave(32000, [300, -300]))
    {
        /// <summary>移植回归验证：Waiting。</summary>
        public TaskCompletionSource<bool> Waiting { get; } = ContinuousVoiceFixture.NewCompletion<bool>();
        /// <summary>移植回归验证：Release。</summary>
        public TaskCompletionSource<bool> Release { get; } = ContinuousVoiceFixture.NewCompletion<bool>();
        /// <summary>移植回归验证：ReadToken。</summary>
        public CancellationToken ReadToken { get; private set; }
        /// <summary>异步读取并校验 OCR 执行诊断或图像快照。</summary>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= Length)
            {
                ReadToken = cancellationToken; Waiting.TrySetResult(true);
                if (await Release.Task.WaitAsync(cancellationToken)) throw new IOException("Controlled interruption");
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
