using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：RoutingTransportTests。</summary>
public sealed class RoutingTransportTests
{
    /// <summary>移植回归验证：Inline progress cancellation prevents next request and allows retry。</summary>
    [Theory]
    [InlineData(TtsPhase.SwitchingWeights, 0)]
    [InlineData(TtsPhase.Synthesizing, 2)]
    public async Task Inline_progress_cancellation_prevents_next_request_and_allows_retry(TtsPhase cancelAt, int expectedGets)
    {
        using var fixture = new RoutingFixture();
        var voice = fixture.LegacyVoice;
        var settings = new AppSettings(fixture.Root, new("http://localhost:19001/"), new(1, 2, 3),
            voice, new(fixture.Root, fixture.Root), new(900, 800));
        var request = new TtsRequest("未经改写。", new(voice.Name, voice.GptWeightsPath, voice.SovitsWeightsPath,
            [new(voice.ReferenceAudioPath, voice.PromptText)]));
        var gets = 0;
        var posts = 0;
        using var engine = new GptSovitsEngine(_ => new HttpClient(new Handler((message, _) =>
        {
            if (message.Method == HttpMethod.Get) { gets++; return Task.FromResult(Success()); }
            posts++;
            return Task.FromResult(Audio());
        })), new ReferenceAudioSelector(), new WavProcessor());
        using var cancellation = new CancellationTokenSource();
        var progress = new TtsTransportTests.InlineProgress<TtsPhase>(phase =>
        {
            if (phase == cancelAt) cancellation.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.SynthesizeAsync(settings, request, progress, cancellation.Token));
        Assert.Equal(expectedGets, gets);
        Assert.Equal(0, posts);
        Assert.NotEmpty((await engine.SynthesizeAsync(settings, request, null, default)).Wav);
        Assert.Equal(expectedGets + 2, gets);
        Assert.Equal(1, posts);
    }

    /// <summary>移植回归验证：Success。</summary>
    internal static HttpResponseMessage Success() => new(HttpStatusCode.OK)
        { Content = new StringContent("{\"message\":\"success\"}") };
    /// <summary>移植回归验证：Audio。</summary>
    internal static HttpResponseMessage Audio() => new(HttpStatusCode.OK)
        { Content = new ByteArrayContent(WavAndReferenceTests.Wave(32000, [200, -200])) };
    /// <summary>移植回归验证：Handler。</summary>
    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        /// <summary>移植回归验证：SendAsync。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
