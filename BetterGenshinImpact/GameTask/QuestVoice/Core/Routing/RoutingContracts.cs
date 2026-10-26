// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;

/// <summary>SpeakerNameStatus：原神自动配音的业务类型。</summary>
public enum SpeakerNameStatus { Accepted, Missing, Rejected }
/// <summary>DialogueContext：原神自动配音的业务类型。</summary>
public enum DialogueContext { Dialogue, Protagonist, Narration }
/// <summary>RoutingLayer：原神自动配音的业务类型。</summary>
public enum RoutingLayer { Protagonist = 1, Narrator, Canonical, Alias, Correction, Fallback }
/// <summary>SpeakerRoutingInput：原神自动配音的业务类型。</summary>
public sealed record SpeakerRoutingInput(string? AcceptedName, SpeakerNameStatus NameStatus,
    DialogueContext Context = DialogueContext.Dialogue);
/// <summary>SpeakerRoute：原神自动配音的业务类型。</summary>
public sealed record SpeakerRoute(RoutingLayer Layer, string CanonicalId, string? VoiceId, string Reason);
/// <summary>VoiceRoutingRequest：原神自动配音的业务类型。</summary>
public sealed record VoiceRoutingRequest(RoutingSettings Settings, SpeakerRoutingInput Input, VoiceSettings FallbackVoice);
/// <summary>VoiceRoutingResult：原神自动配音的业务类型。</summary>
public sealed record VoiceRoutingResult(SpeakerRoutingInput Input, SpeakerRoute Speaker, string CatalogSha256,
    string? RequestedVoiceId, string VoiceId, VoiceBinding Voice, string? RequestedEmotion, string? FallbackReason,
    string? CanonicalName = null);

/// <summary>ISpeakerRouter：原神自动配音的业务类型。</summary>
public interface ISpeakerRouter
{
    /// <summary>依次按主角、旁白、规范姓名、别名、纠错与默认 NPC 解析声线。</summary>
    SpeakerRoute Route(SpeakerRoutingInput input, RoutingCatalog catalog);
}

/// <summary>IVoiceRoutingService：原神自动配音的业务类型。</summary>
public interface IVoiceRoutingService
{
    /// <summary>按接受姓名解析角色和实际声线，保留兜底原因。</summary>
    Task<VoiceRoutingResult> ResolveAsync(VoiceRoutingRequest request, CancellationToken cancellationToken);
}
