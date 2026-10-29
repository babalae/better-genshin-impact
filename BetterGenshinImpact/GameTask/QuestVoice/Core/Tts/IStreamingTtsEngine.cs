using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

/// <summary>可选流式能力；Ready仅移交播放引用，返回任务仍拥有HTTP直到EOF。</summary>
public interface IStreamingTtsEngine : ITtsEngine
{
    /// <summary>混合声线按本次请求判断；已有单引擎实现保持原行为。</summary>
    bool CanStream(TtsRequest request) => true;
    /// <summary>有效PCM水位就绪时通知，取消不关闭已发请求；返回完整校验结果。</summary>
    Task<TtsResult> SynthesizeStreamingAsync(AppSettings settings, TtsRequest request,
        Action<StreamingPcmAudio> ready, IProgress<TtsPhase>? progress, CancellationToken cancellationToken);
}
