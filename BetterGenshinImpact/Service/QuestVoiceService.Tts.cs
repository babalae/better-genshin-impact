using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using NAudio.Wave;

namespace BetterGenshinImpact.Service;

/// <summary>用户主动触发的独立 TTS 测试，沿用服务操作锁和生产播放器。</summary>
public sealed partial class QuestVoiceService
{
    /// <summary>只在操作锁内创建/释放，停止命令在捕获锁内请求取消。</summary>
    private CancellationTokenSource? _testCancellation;
    /// <summary>退出可以关闭测试请求；普通取消仅请求丢弃并保留 GPT 排空。</summary>
    private ITtsProvider? _testProvider;

    /// <summary>无需游戏或 OCR 即可测试当前默认声线；与正式会话互斥。</summary>
    public Task TestSpeechAsync(string text, CancellationToken token = default) => TestSpeechCoreAsync(text, null, token);

    /// <summary>试听选中声线草稿，复用正式测试的互斥、取消、排空与播放器。</summary>
    public Task TestVoiceAsync(string text, RoutingVoice profile, CancellationToken token = default)
        => TestSpeechCoreAsync(text, profile, token);

    /// <summary>两个测试入口共享唯一的播放与后端资源所有权。</summary>
    private async Task TestSpeechCoreAsync(string text, RoutingVoice? profile, CancellationToken token)
    {
        await _operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Status.IsRunning || Status.IsBusy) throw new InvalidOperationException("请先停止自动配音再测试语音。");
            if (Status.BackendUnknown) throw new InvalidOperationException("后端状态未知，请先手动恢复并确认。");
            var settings = profile is null ? _factory.CreateTtsSettings() : _factory.CreateVoiceSettings(profile);
            if (profile is not null)
            {
                var reason = await RoutingVoiceAssets.UnavailableAsync(profile, token, _factory.RoutingServerSidePaths).ConfigureAwait(false);
                if (reason is not null) throw new VoiceException(VoiceErrorCode.Configuration, reason);
            }
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
}
