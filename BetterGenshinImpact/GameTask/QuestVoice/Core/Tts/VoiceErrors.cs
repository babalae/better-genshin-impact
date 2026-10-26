// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
/// <summary>VoiceErrors：原神自动配音的业务类型。</summary>
public static class VoiceErrors
{
    /// <summary>将错误码映射到明确的诊断责任边界。</summary>
    public static VoiceErrorOwner Owner(VoiceErrorCode code) => code switch
    {
        VoiceErrorCode.Configuration or VoiceErrorCode.Internal => VoiceErrorOwner.Client,
        VoiceErrorCode.Unreachable or VoiceErrorCode.Timeout or VoiceErrorCode.WeightRejected
            or VoiceErrorCode.SynthesisRejected or VoiceErrorCode.InterruptedResponse
            or VoiceErrorCode.InvalidAudio or VoiceErrorCode.BackendUnknown => VoiceErrorOwner.Backend,
        VoiceErrorCode.SaveFailed => VoiceErrorOwner.Storage,
        VoiceErrorCode.PlaybackDevice => VoiceErrorOwner.Playback,
        VoiceErrorCode.Cancelled => VoiceErrorOwner.User,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Error owner must be classified explicitly.")
    };
}
