// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

/// <summary>VoiceErrorCode：原神自动配音的业务类型。</summary>
public enum VoiceErrorCode
{
    Configuration, Internal, Unreachable, Timeout, WeightRejected, SynthesisRejected,
    InterruptedResponse, InvalidAudio, SaveFailed, PlaybackDevice, Cancelled, BackendUnknown
}
/// <summary>VoiceErrorOwner：原神自动配音的业务类型。</summary>
public enum VoiceErrorOwner { Client, Backend, Storage, Playback, User }
/// <summary>VoiceException：原神自动配音的业务类型。</summary>
public sealed class VoiceException(VoiceErrorCode code, string message, bool backendUnknown = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>Code：当前业务对象的配置、身份或状态值。</summary>
    public VoiceErrorCode Code { get; } = code;
    /// <summary>BackendUnknown：当前业务对象的配置、身份或状态值。</summary>
    public bool BackendUnknown { get; } = backendUnknown;
}
