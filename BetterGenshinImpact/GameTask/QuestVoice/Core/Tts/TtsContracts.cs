// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

/// <summary>TtsPhase：原神自动配音的业务类型。</summary>
public enum TtsPhase { SwitchingWeights, Synthesizing, Draining }
/// <summary>VoiceState：原神自动配音的业务类型。</summary>
public enum VoiceState { Ready, Synthesizing, Playing, Draining, Completed, Cancelled, Failed, BackendUnknown, Recognizing, OcrCancelling, OcrCompleted, SelectingScene, SceneRejected, SceneUnknown, NoSpeakableText, Routing, RoutingCancelling }
/// <summary>ReferenceFallback：原神自动配音的业务类型。</summary>
public enum ReferenceFallback { None, CharacterHasNoEmotionTags, EmotionNotAvailable }
/// <summary>ReferenceAudio：原神自动配音的业务类型。</summary>
public sealed record ReferenceAudio(string Path, string PromptText, string? Emotion = null);
/// <summary>VoiceBinding：原神自动配音的业务类型。</summary>
public sealed record VoiceBinding(string Name, string GptWeightsPath, string SovitsWeightsPath,
    IReadOnlyList<ReferenceAudio> References, TtsProviderSettings? Provider = null);
/// <summary>ReferenceSelection：原神自动配音的业务类型。</summary>
public sealed record ReferenceSelection(ReferenceAudio Audio, ReferenceFallback Fallback);
/// <summary>TtsRequest：原神自动配音的业务类型。</summary>
public sealed record TtsRequest(string Text, VoiceBinding Voice, string? Emotion = null);
/// <summary>TtsResult：原神自动配音的业务类型。</summary>
public sealed record TtsResult(byte[] Wav, double SynthesisSeconds, double TimeToFirstByteSeconds,
    double AudioSeconds, int SampleRate, int Channels, ReferenceFallback ReferenceFallback,
    TtsAudioEncoding SourceEncoding = TtsAudioEncoding.Pcm16Wav);
/// <summary>VoiceStatus：原神自动配音的业务类型。</summary>
public sealed record VoiceStatus(VoiceState State, string? Detail = null, string? ArtifactPath = null,
    VoiceErrorCode? Error = null);

/// <summary>IReferenceAudioSelector：原神自动配音的业务类型。</summary>
public interface IReferenceAudioSelector
{
    /// <summary>依据颜色、分隔线和字形几何证据选择对白布局。</summary>
    ReferenceSelection Select(VoiceBinding voice, string? emotion);
}
/// <summary>IWavProcessor：原神自动配音的业务类型。</summary>
public interface IWavProcessor
{
    /// <summary>校验完整 PCM16 WAV 首部、长度、采样率和非静音样本。</summary>
    WavAudio ValidateCompletedResponse(byte[] bytes);
}
/// <summary>WavAudio：原神自动配音的业务类型。</summary>
public sealed record WavAudio(byte[] Bytes, double DurationSeconds, int SampleRate, int Channels);
/// <summary>ITtsEngine：原神自动配音的业务类型。</summary>
public interface ITtsEngine
{
    /// <summary>串行切换声线权重并读取完整语音响应，取消后丢弃结果。</summary>
    Task<TtsResult> SynthesizeAsync(AppSettings settings, TtsRequest request,
        IProgress<TtsPhase>? progress, CancellationToken cancellationToken);
    /// <summary>中断本地 HTTP 等待，不向后端发送关闭指令。</summary>
    void Shutdown();
}
/// <summary>IAudioArtifactStore：原神自动配音的业务类型。</summary>
public interface IAudioArtifactStore
{
    /// <summary>将完整音频先写临时文件，再原子移动到本轮目录。</summary>
    Task<string> SaveAsync(string directory, byte[] wav, CancellationToken cancellationToken);
}
