// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
/// <summary>ReferenceAudioSelector：原神自动配音的业务类型。</summary>
public sealed class ReferenceAudioSelector : IReferenceAudioSelector
{
    /// <summary>依据颜色、分隔线和字形几何证据选择对白布局。</summary>
    public ReferenceSelection Select(VoiceBinding voice, string? emotion)
    {
        if (voice.References.Count == 0)
            throw new VoiceException(VoiceErrorCode.Configuration, "声线没有参考音频。");
        var fallback = voice.References[0];
        if (string.IsNullOrWhiteSpace(emotion)) return new(fallback, ReferenceFallback.None);
        var match = voice.References.FirstOrDefault(audio => string.Equals(audio.Emotion, emotion, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return new(match, ReferenceFallback.None);
        return new(fallback, voice.References.Any(audio => !string.IsNullOrWhiteSpace(audio.Emotion))
            ? ReferenceFallback.EmotionNotAvailable : ReferenceFallback.CharacterHasNoEmotionTags);
    }
}
