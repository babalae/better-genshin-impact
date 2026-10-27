using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>会话拥有的声线分派器；底层 Provider 继续拥有协议、排空及未知状态。</summary>
public sealed class RoutedTtsProvider(Func<VoiceBinding, AppSettings> resolve,
    TtsProviderInfo defaultInfo, Func<string, ITtsProvider>? create = null) : ITtsProvider, IStreamingTtsEngine
{
    /// <summary>退出与实例创建使用同一锁，避免退出时漏掉迟到实例。</summary>
    private readonly object _gate = new();
    /// <summary>连接实例拥有独立后端状态，同实例不同声线复用安全切权状态。</summary>
    private readonly Dictionary<(string Provider, string Instance), ITtsProvider> _providers = [];
    /// <summary>退出后拒绝任何新请求。</summary>
    private bool _stopped;
    /// <summary>释放幂等，避免重复处理底层 HTTP 生命周期。</summary>
    private bool _disposed;
    /// <summary>默认引擎仅用于通用诊断，逐句选择由声线引用决定。</summary>
    public TtsProviderInfo Info => defaultInfo;
    /// <summary>逐句分派，不捕获或重新解释底层取消与 BackendUnknown。</summary>
    public Task<TtsResult> SynthesizeAsync(AppSettings settings, TtsRequest request, IProgress<TtsPhase>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = Select(settings, request);
        return Get(selected).SynthesizeAsync(selected, request, progress, cancellationToken);
    }
    /// <summary>逐请求判断实际流式能力。</summary>
    public bool CanStream(TtsRequest request) => TtsProviderRegistry.GetInfo(resolve(request.Voice).Provider!.ProviderId).Capabilities.Streaming;
    /// <summary>将流式 Ready 所有权直接传给原实现。</summary>
    public Task<TtsResult> SynthesizeStreamingAsync(AppSettings settings, TtsRequest request, Action<StreamingPcmAudio> ready,
        IProgress<TtsPhase>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = Select(settings, request);
        if (Get(selected) is not IStreamingTtsEngine streaming)
            throw new VoiceException(VoiceErrorCode.Configuration, "所选声线的引擎不支持流式。");
        return streaming.SynthesizeStreamingAsync(selected, request, ready, progress, cancellationToken);
    }
    /// <summary>验证默认配置。</summary>
    public void Validate(AppSettings settings) => Get(settings).Validate(settings);
    /// <summary>默认引擎声音列表。</summary>
    public Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(AppSettings settings, CancellationToken token) => Get(settings).GetVoicesAsync(settings, token);
    /// <summary>只读默认实例探测。</summary>
    public Task<string> ProbeAsync(AppSettings settings, CancellationToken token) => Get(settings).ProbeAsync(settings, token);
    /// <summary>退出时中止所有已创建实例。</summary>
    public void Shutdown()
    {
        lock (_gate)
        {
            _stopped = true;
            foreach (var provider in _providers.Values) provider.Shutdown();
        }
    }
    /// <summary>释放所有本轮实例。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _stopped = true;
            foreach (var provider in _providers.Values) provider.Dispose();
        }
    }

    /// <summary>声线快照来自冻结配置，输出目录和播放协议属于当前会话。</summary>
    private AppSettings Select(AppSettings settings, TtsRequest request) => resolve(request.Voice) with
        { Output = settings.Output, Protocol = settings.Protocol };

    /// <summary>同一实例保留原 Provider，不在逐句合成之间丢失排空/未知状态。</summary>
    private ITtsProvider Get(AppSettings settings)
    {
        var reference = settings.Provider ?? new();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopped) throw new OperationCanceledException("TTS 会话已关闭。");
            var key = (reference.ProviderId, reference.InstanceId);
            if (!_providers.TryGetValue(key, out var provider))
                _providers.Add(key, provider = (create ?? TtsProviderRegistry.Create)(reference.ProviderId));
            provider.Validate(settings);
            return provider;
        }
    }
}
