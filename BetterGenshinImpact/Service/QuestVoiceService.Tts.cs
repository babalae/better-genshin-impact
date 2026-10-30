using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using NAudio.Wave;

namespace BetterGenshinImpact.Service;

/// <summary>设置界面使用的试听、声音列表和连接测试；ViewModel 只调用这里，不直接访问引擎。</summary>
public sealed partial class QuestVoiceService
{
    /// <summary>只在操作锁内创建/释放，停止命令在捕获锁内请求取消。</summary>
    private CancellationTokenSource? _testCancellation;
    /// <summary>退出可以关闭测试请求；普通取消仅请求丢弃并保留 GPT 排空。</summary>
    private ITtsProvider? _testProvider;

    /// <summary>试听指定声线（可以是未保存的草稿），与正式会话互斥并共用取消与排空。</summary>
    public async Task TestVoiceAsync(string text, RoutingVoice profile, CancellationToken token = default)
    {
        await _operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Status.IsRunning || Status.IsBusy) throw new InvalidOperationException("请先停止自动配音再测试语音。");
            if (Status.BackendUnknown) throw new InvalidOperationException("后端状态未知，请先手动恢复并确认。");
            var settings = _factory.CreateVoiceSettings(profile);
            var reason = await RoutingVoiceAssets.UnavailableAsync(profile, token, _factory.RoutingServerSidePaths).ConfigureAwait(false);
            if (reason is not null) throw new VoiceException(VoiceErrorCode.Configuration, reason);
            settings = settings with { Protocol = settings.Protocol! with { StreamingMode = false, StreamPlayback = false } };
            using var provider = TtsProviderRegistry.Create(settings.Provider!.ProviderId);
            using var player = new WasapiAudioPlayer(() => new WasapiOut());
            lock (_captureGate)
            {
                _testCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                _testProvider = provider;
            }
            Update(s => s with { IsBusy = true, Tts = "正在测试合成", Error = "" });
            try
            {
                var voice = settings.Voice;
                var request = new TtsRequest(text, new(voice.Name, voice.GptWeightsPath, voice.SovitsWeightsPath,
                    [new(voice.ReferenceAudioPath, voice.PromptText)], settings.Provider));
                // 测试入口完整接收后播放；正式流式能力仍由原会话及其 Ready 机制验收。
                var result = await provider.SynthesizeAsync(settings, request, null, _testCancellation.Token).ConfigureAwait(false);
                Update(s => s with { Tts = $"合成完成：{result.AudioSeconds:F2}s · {result.SampleRate} Hz · {result.SourceEncoding}", Playback = "正在播放测试语音" });
                await player.PlayAsync(result.Wav, _testCancellation.Token).ConfigureAwait(false);
                Update(s => s with { Playback = "测试语音播放完成" });
            }
            catch (VoiceException exception)
            {
                Update(s => s with { BackendUnknown = s.BackendUnknown || exception.BackendUnknown,
                    Tts = "测试合成或播放失败", Error = exception.Message });
                throw;
            }
            catch (OperationCanceledException)
            {
                Update(s => s with { Tts = "测试已取消；在途请求已收尾", Playback = "已停止" });
                throw;
            }
            finally
            {
                lock (_captureGate) { _testProvider = null; _testCancellation.Dispose(); _testCancellation = null; }
                Update(s => s with { IsBusy = false });
            }
        }
        finally { _operations.Release(); }
    }

    /// <summary>立即取消测试播放；GPT 已发请求仍由 Provider 排空。</summary>
    public void CancelTestSpeech()
    { lock (_captureGate) _testCancellation?.Cancel(); }

    /// <summary>读取指定引擎实例的声音列表；只有 VOICEVOX、VITS 等服务端引擎会联网。</summary>
    public async Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(AppSettings settings, CancellationToken token = default)
    {
        using var provider = TtsProviderRegistry.Create(settings.Provider!.ProviderId);
        if (!provider.Info.Capabilities.PresetVoices) return Array.Empty<TtsVoiceInfo>();
        return await provider.GetVoicesAsync(settings, token).ConfigureAwait(false);
    }

    /// <summary>只读探测服务可达性，不合成、不切换权重，成功也不代表后端空闲。</summary>
    public async Task<string> ProbeAsync(AppSettings settings, CancellationToken token = default)
    {
        using var provider = TtsProviderRegistry.Create(settings.Provider!.ProviderId);
        return await provider.ProbeAsync(settings, token).ConfigureAwait(false);
    }
}
