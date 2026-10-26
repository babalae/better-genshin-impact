// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;

/// <summary>MissingVoicePolicy：原神自动配音的业务类型。</summary>
public enum MissingVoicePolicy { Fallback, Fail }

/// <summary>RoutingCatalog：原神自动配音的业务类型。</summary>
public sealed class RoutingCatalog(int version, string game, string? protagonistId,
    IEnumerable<string> playerNames, string? narratorVoiceId, string? fallbackVoiceId,
    MissingVoicePolicy missingVoicePolicy, IEnumerable<SpeakerDefinition> speakers, IEnumerable<RoutingVoice> voices)
{
    /// <summary>保存 ConfiguredVoiceId 对应的本轮状态或配置，不跨会话复用。</summary>
    public const string ConfiguredVoiceId = "configured";
    /// <summary>Version：当前业务对象的配置、身份或状态值。</summary>
    public int Version { get; } = version;
    /// <summary>Game：当前业务对象的配置、身份或状态值。</summary>
    public string Game { get; } = game;
    /// <summary>ProtagonistId：当前业务对象的配置、身份或状态值。</summary>
    public string? ProtagonistId { get; } = protagonistId;
    /// <summary>PlayerNames：当前业务对象的配置、身份或状态值。</summary>
    public IReadOnlyList<string> PlayerNames { get; } = Array.AsReadOnly(playerNames.ToArray());
    /// <summary>NarratorVoiceId：当前业务对象的配置、身份或状态值。</summary>
    public string? NarratorVoiceId { get; } = narratorVoiceId;
    /// <summary>FallbackVoiceId：当前业务对象的配置、身份或状态值。</summary>
    public string? FallbackVoiceId { get; } = fallbackVoiceId;
    /// <summary>MissingVoicePolicy：当前业务对象的配置、身份或状态值。</summary>
    public MissingVoicePolicy MissingVoicePolicy { get; } = missingVoicePolicy;
    /// <summary>Speakers：当前业务对象的配置、身份或状态值。</summary>
    public IReadOnlyList<SpeakerDefinition> Speakers { get; } = Array.AsReadOnly(speakers.ToArray());
    /// <summary>Voices：当前业务对象的配置、身份或状态值。</summary>
    public IReadOnlyList<RoutingVoice> Voices { get; } = Array.AsReadOnly(voices.ToArray());
}

/// <summary>SpeakerDefinition：原神自动配音的业务类型。</summary>
public sealed class SpeakerDefinition(string id, IEnumerable<string> names, IEnumerable<string> aliases,
    IEnumerable<string> corrections, string? voiceId)
{
    /// <summary>Id：当前业务对象的配置、身份或状态值。</summary>
    public string Id { get; } = id;
    /// <summary>Names：当前业务对象的配置、身份或状态值。</summary>
    public IReadOnlyList<string> Names { get; } = Array.AsReadOnly(names.ToArray());
    /// <summary>Aliases：当前业务对象的配置、身份或状态值。</summary>
    public IReadOnlyList<string> Aliases { get; } = Array.AsReadOnly(aliases.ToArray());
    /// <summary>Corrections：当前业务对象的配置、身份或状态值。</summary>
    public IReadOnlyList<string> Corrections { get; } = Array.AsReadOnly(corrections.ToArray());
    /// <summary>VoiceId：当前业务对象的配置、身份或状态值。</summary>
    public string? VoiceId { get; } = voiceId;
}

/// <summary>RoutingVoice：原神自动配音的业务类型。</summary>
public sealed record RoutingVoice(string Id, string Name, string GptWeightsPath, string SovitsWeightsPath,
    string ReferenceAudioPath, string PromptText, string? RequestedEmotion = null, string? ReferenceEmotion = null,
    string ProviderId = "gpt-sovits", string? InstanceId = null, string? ProviderVoiceId = null);

/// <summary>RoutingCatalogSnapshot：原神自动配音的业务类型。</summary>
public sealed record RoutingCatalogSnapshot(RoutingCatalog Catalog, string CatalogSha256, string CatalogPath);
