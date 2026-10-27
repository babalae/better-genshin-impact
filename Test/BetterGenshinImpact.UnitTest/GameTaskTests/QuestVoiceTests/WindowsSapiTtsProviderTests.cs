using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>Windows SAPI 的注册、真实系统声音和内存音频边界。</summary>
public sealed class WindowsSapiTtsProviderTests
{
    /// <summary>不依赖网络、GPT 权重或参考音频的系统声音配置。</summary>
    private static AppSettings Settings => new(".", new Uri("http://127.0.0.1/"), new(1, 1, 10),
        new("系统", "", "", "", ""), new(".", "."), new(0, 0), Provider: new("windows-sapi"));
    /// <summary>与系统语音引擎交互的短句，不含 SSML。</summary>
    private static TtsRequest Request => new("你好，旅行者。Hello traveler.", new("系统", "", "", []));
    /// <summary>内置离线声音应独立于服务器、参考资产及流式协议。</summary>
    [Fact]
    public void RegistryDeclaresOfflineSystemVoices()
    {
        var info = TtsProviderRegistry.GetInfo("windows-sapi");
        Assert.True(info.Capabilities.PresetVoices);
        Assert.False(info.Capabilities.ServerConnection);
        Assert.False(info.Capabilities.VoiceAssets);
        Assert.False(info.Capabilities.Streaming);
        Assert.False(info.Capabilities.RequiresDrain);
    }

    /// <summary>刷新系统音色无需先有有效声音 ID，也不接触 GPT 文件。</summary>
    [Fact]
    public async Task EnumeratesInstalledTokensWithoutSelectedVoice()
    {
        using var provider = new WindowsSapiTtsProvider();
        var voices = await provider.GetVoicesAsync(Settings, default);
        Assert.NotEmpty(voices);
        Assert.All(voices, voice =>
        {
            Assert.False(string.IsNullOrWhiteSpace(voice.Id));
            Assert.False(string.IsNullOrWhiteSpace(voice.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(voice.Language));
        });
        Assert.Equal(voices.Count, voices.Select(voice => voice.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>系统token可在多个COM调用间共享RCW，自动/手动刷新并发不能强制释放彼此对象。</summary>
    [Fact]
    public async Task ConcurrentProvidersCanEnumerateAndSynthesizeWithoutReleasingEachOthersTokens()
    {
        using var first = new WindowsSapiTtsProvider();
        using var second = new WindowsSapiTtsProvider();
        var installed = await first.GetVoicesAsync(Settings, default);
        var selected = Settings with { Provider = new("windows-sapi", VoiceId: installed[0].Id) };
        for (var iteration = 0; iteration < 12; iteration++)
        {
            await Task.WhenAll(
                first.GetVoicesAsync(Settings, default),
                second.GetVoicesAsync(Settings, default),
                first.GetVoicesAsync(Settings, default),
                second.SynthesizeAsync(selected, Request, null, default));
        }
    }

    /// <summary>另一实例占用COM时，取消排队的枚举须及时返回且不能释放持有者的门锁。</summary>
    [Fact]
    public async Task CancellingQueuedComWorkDoesNotAffectCurrentOwner()
    {
        using var first = new WindowsSapiTtsProvider();
        using var second = new WindowsSapiTtsProvider();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var voices = await first.GetVoicesAsync(Settings, default);
        var settings = Settings with { Provider = new("windows-sapi", VoiceId: voices[0].Id) };
        var synthesis = first.SynthesizeAsync(settings, Request, new InlineProgress(_ =>
        { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); }), default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var queued = second.GetVoicesAsync(Settings, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { release.Set(); await synthesis; }
        Assert.NotEmpty(await second.GetVoicesAsync(Settings, default));
    }

    /// <summary>每个实际安装的声音都输出非静音 PCM16 WAV，全部音频只在内存中生成。</summary>
    [Fact]
    public async Task InstalledVoicesProduceValidatedPcm16Wav()
    {
        using var provider = new WindowsSapiTtsProvider();
        var voices = await provider.GetVoicesAsync(Settings, default);
        Assert.NotEmpty(voices);
        foreach (var voice in voices)
        {
            var settings = Settings with { Provider = new("windows-sapi", VoiceId: voice.Id) };
            provider.Validate(settings);
            var result = await provider.SynthesizeAsync(settings, Request, null, default);
            var audio = new WavProcessor().ValidateCompletedResponse(result.Wav);
            Assert.Equal(22050, audio.SampleRate);
            Assert.Equal(1, audio.Channels);
            Assert.True(audio.DurationSeconds > 0);
            Assert.Equal(TtsAudioEncoding.Pcm16Wav, result.SourceEncoding);
            Assert.Equal(audio.DurationSeconds, result.AudioSeconds);
        }
    }

    /// <summary>保存的声音已卸载时明确失败，不能合成系统默认声音。</summary>
    [Fact]
    public async Task MissingTokenNeverFallsBackToDefaultVoice()
    {
        using var provider = new WindowsSapiTtsProvider();
        var settings = Settings with { Provider = new("windows-sapi", VoiceId: "missing-sapi-token") };
        var error = await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(settings, Request, null, default));
        Assert.Equal(VoiceErrorCode.Configuration, error.Code);
        Assert.False(error.BackendUnknown);
    }

    /// <summary>不合法的倍率不能进入原生引擎。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.49)]
    [InlineData(2.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidRateIsRejected(double rate)
    {
        using var provider = new WindowsSapiTtsProvider();
        var settings = Settings with { Provider = new("windows-sapi", VoiceId: "token"), Extra = new(Rate: rate) };
        var error = Assert.Throws<VoiceException>(() => provider.Validate(settings));
        Assert.Equal(VoiceErrorCode.Configuration, error.Code);
    }

    /// <summary>预取消请求不进入 COM，取消一次后实例仍可正常枚举声音。</summary>
    [Fact]
    public async Task CancellationDoesNotPoisonLaterRequests()
    {
        using var provider = new WindowsSapiTtsProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetVoicesAsync(Settings, cancellation.Token));
        var voices = await provider.GetVoicesAsync(Settings, default);
        var settings = Settings with { Provider = new("windows-sapi", VoiceId: voices[0].Id) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.SynthesizeAsync(settings, Request, null, cancellation.Token));
        Assert.NotEmpty(await provider.GetVoicesAsync(Settings, default));
    }

    /// <summary>在实际长句合成开始后取消，等待原 STA 清理结束，再用同实例合成短句。</summary>
    [Fact]
    public async Task CancellingActiveSynthesisAllowsNextSynthesis()
    {
        using var provider = new WindowsSapiTtsProvider();
        var voices = await provider.GetVoicesAsync(Settings, default);
        var settings = Settings with { Provider = new("windows-sapi", VoiceId: voices[0].Id) };
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cancellation.CancelAfter(TimeSpan.FromMilliseconds(25)));
        var request = Request with { Text = string.Concat(Enumerable.Repeat("你好，旅行者。", 250)) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.SynthesizeAsync(settings, request, progress, cancellation.Token));
        var result = await provider.SynthesizeAsync(settings, Request, null, default);
        Assert.True(new WavProcessor().ValidateCompletedResponse(result.Wav).DurationSeconds > 0);
    }

    /// <summary>关闭本实例后不再开始新的原生工作。</summary>
    [Fact]
    public async Task ShutdownCancelsInstanceWork()
    {
        using var provider = new WindowsSapiTtsProvider();
        provider.Shutdown();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetVoicesAsync(Settings, default));
    }

    /// <summary>超长或空台词不进入原生合成。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2001)]
    public async Task InvalidTextIsRejected(int length)
    {
        using var provider = new WindowsSapiTtsProvider();
        var settings = Settings with { Provider = new("windows-sapi", VoiceId: "token") };
        var request = Request with { Text = new string('你', length) };
        var error = await Assert.ThrowsAsync<VoiceException>(() => provider.SynthesizeAsync(settings, request, null, default));
        Assert.Equal(VoiceErrorCode.Configuration, error.Code);
    }

    /// <summary>同步通知仅用于在真实合成发起时安排取消，不替换 SAPI 实现。</summary>
    private sealed class InlineProgress(Action<TtsPhase> report) : IProgress<TtsPhase>
    {
        /// <summary>在合成工作线程执行观察回调。</summary>
        public void Report(TtsPhase value) => report(value);
    }
}
