using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>验证 Provider/实例/声音的真实分派边界及资源所有权。</summary>
public sealed class RoutedTtsProviderTests
{
    /// <summary>GPT profile 的 Ready 引用与 EOF 必须直接沿用底层流式协议。</summary>
    [Fact]
    public async Task Streaming_profile_preserves_ready_source_and_waits_for_underlying_completion()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoutedStream", Guid.NewGuid().ToString("N"));
        var backend = new StreamingProvider(root);
        using var routed = new RoutedTtsProvider(binding => Settings(binding.Provider!), TtsProviderRegistry.GetInfo("huoshan"), _ => backend);
        var request = new TtsRequest("流式", new("GPT角色", "", "", [], new("gpt-sovits", "remote", "")));
        StreamingPcmAudio? retained = null;
        Assert.True(routed.CanStream(request));
        var pending = routed.SynthesizeStreamingAsync(Settings(new("huoshan")), request,
            source => { source.Retain(); retained = source; }, null, default);
        await backend.Ready.Task;
        Assert.False(pending.IsCompleted);
        Assert.Same(backend.Source, retained);
        backend.EndOfResponse.SetResult();
        await pending;
        Assert.True(retained!.IsReady);
        retained.Dispose();
        Assert.Empty(Directory.GetFiles(root));
        Directory.Delete(root);
    }

    /// <summary>不同 profile 不串音，同实例复用 Provider，取消和 Unknown 原样传播。</summary>
    [Fact]
    public async Task Dispatch_preserves_voice_instance_cancellation_and_unknown()
    {
        var made = new List<RecordingProvider>();
        var defaults = Settings(new("huoshan", "local", "default"));
        using var routed = new RoutedTtsProvider(binding => Settings(binding.Provider!), TtsProviderRegistry.GetInfo("huoshan"),
            id => { var provider = new RecordingProvider(id); made.Add(provider); return provider; });
        TtsRequest Request(string id, string instance, string voice) => new("你好", new("角色声线", "", "", [], new(id, instance, voice)));
        await routed.SynthesizeAsync(defaults, Request("huoshan", "local", "one"), null, default);
        await routed.SynthesizeAsync(defaults, Request("gpt-sovits", "remote", "two"), null, default);
        await routed.SynthesizeAsync(defaults, Request("huoshan", "local", "three"), null, default);
        Assert.Equal(2, made.Count);
        Assert.Equal(new[] { "one", "three" }, made[0].VoiceIds);
        Assert.Equal("remote", made[1].LastSettings!.Provider!.InstanceId);
        Assert.False(routed.CanStream(Request("huoshan", "local", "one")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => routed.SynthesizeAsync(defaults, Request("huoshan", "local", "one"), null, new(true)));
        made[1].Failure = new VoiceException(VoiceErrorCode.BackendUnknown, "unknown", backendUnknown: true);
        var error = await Assert.ThrowsAsync<VoiceException>(() => routed.SynthesizeAsync(defaults, Request("gpt-sovits", "remote", "two"), null, default));
        Assert.Same(made[1].Failure, error);
        routed.Shutdown();
        Assert.All(made, provider => Assert.True(provider.Stopped));
        routed.Dispose();
        Assert.All(made, provider => Assert.Equal(1, provider.DisposeCount));
    }

    /// <summary>构造不依赖文件或网络的启动快照。</summary>
    private static AppSettings Settings(TtsProviderSettings provider) => new("", new("http://localhost/"), new(1, 1, 1),
        new("voice", "", "", "", ""), new("", ""), new(0, 0), Provider: provider);

    /// <summary>记录实际收到的快照，不模拟分派器自身行为。</summary>
    private class RecordingProvider(string id) : ITtsProvider
    {
        /// <summary>该实例所代表的引擎。</summary>
        public TtsProviderInfo Info => TtsProviderRegistry.GetInfo(id);
        /// <summary>收到的声音标识。</summary>
        public List<string> VoiceIds { get; } = [];
        /// <summary>最近一次完整快照。</summary>
        public AppSettings? LastSettings { get; private set; }
        /// <summary>注入后端状态错误以验证不被包装或吞掉。</summary>
        public Exception? Failure { get; set; }
        /// <summary>退出通知已发生。</summary>
        public bool Stopped { get; private set; }
        /// <summary>实例只释放一次。</summary>
        public int DisposeCount { get; private set; }
        /// <summary>保存实际分派参数，取消先于发送。</summary>
        public Task<TtsResult> SynthesizeAsync(AppSettings settings, TtsRequest request, IProgress<TtsPhase>? progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) return Task.FromException<TtsResult>(Failure);
            LastSettings = settings; VoiceIds.Add(settings.Provider!.VoiceId);
            return Task.FromResult(new TtsResult([], 0, 0, 0, 24000, 1, ReferenceFallback.None));
        }
        /// <summary>参数验证由实际 Provider 另有协议测试覆盖。</summary>
        public void Validate(AppSettings settings) { }
        /// <summary>无远程声音列表。</summary>
        public Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(AppSettings settings, CancellationToken token) => Task.FromResult<IReadOnlyList<TtsVoiceInfo>>([]);
        /// <summary>被动探测。</summary>
        public Task<string> ProbeAsync(AppSettings settings, CancellationToken token) => Task.FromResult("test");
        /// <summary>记录退出。</summary>
        public void Shutdown() => Stopped = true;
        /// <summary>记录释放次数。</summary>
        public void Dispose() => DisposeCount++;
    }

    /// <summary>可控 Ready/EOF 流式后端，只隔离网络，实际使用生产 PCM 源所有权。</summary>
    private sealed class StreamingProvider(string root) : RecordingProvider("gpt-sovits"), IStreamingTtsEngine
    {
        /// <summary>通知调用方已收到 Ready。</summary>
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>由测试释放正常 EOF。</summary>
        public TaskCompletionSource EndOfResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>底层真实 PCM 源。</summary>
        public StreamingPcmAudio? Source { get; private set; }
        /// <summary>传输持有首引用，Ready 使用者单独 Retain。</summary>
        public async Task<TtsResult> SynthesizeStreamingAsync(AppSettings settings, TtsRequest request, Action<StreamingPcmAudio> ready,
            IProgress<TtsPhase>? progress, CancellationToken cancellationToken)
        {
            using var source = Source = new StreamingPcmAudio(root, 24000, 1);
            await source.AppendAsync(new byte[] { 1, 0, 255, 255 }, cancellationToken);
            source.Complete(); ready(source); Ready.SetResult();
            await EndOfResponse.Task.WaitAsync(cancellationToken);
            return new(source.ToWav(), 0, 0, .001, 24000, 1, ReferenceFallback.None);
        }
    }
}
