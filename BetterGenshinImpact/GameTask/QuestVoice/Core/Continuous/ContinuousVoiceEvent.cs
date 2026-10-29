// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;

/// <summary>ContinuousVoiceEventKind：原神自动配音的业务类型。</summary>
public enum ContinuousVoiceEventKind
{
    Started, Invalidated, CandidateOffered, RoutingStarted, Routed, Duplicate,
    SynthesisStarted, SynthesisDraining, SynthesisCompleted, Saving, AudioReady,
    Discarded, PlaybackStarted, StopRequested, PlaybackStopFailed, PlaybackCompleted, CandidateFailed,
    LeftDialogue, Completing, Stopping, Completed
}

/// <summary>Elapsed is monotonic session time; StopRequested never implies playback exited.</summary>
public sealed record ContinuousVoiceEvent(long Sequence, TimeSpan Elapsed, ContinuousVoiceEventKind Kind,
    long Revision, long Epoch, long? SourceSequence, ContinuousVoiceWork? Work = null,
    string? Detail = null, VoiceErrorCode? Error = null, bool PlaybackSucceeded = false, bool BackendUnknown = false);
