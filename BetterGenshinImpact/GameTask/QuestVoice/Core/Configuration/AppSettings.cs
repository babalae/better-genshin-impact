// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;

/// <summary>AppSettings：原神自动配音的业务类型。</summary>
public sealed record AppSettings(
    string ConfigurationDirectory,
    Uri TtsServiceUrl,
    TimeoutSettings Timeouts,
    VoiceSettings Voice,
    OutputSettings Output,
    WindowSettings Window,
    TtsProtocolSettings? Protocol = null, OcrSettings? Ocr = null, string? OcrConfigurationError = null,
    SceneSettings? Scene = null, string? SceneConfigurationError = null,
    RoutingSettings? Routing = null, string? RoutingConfigurationError = null, TtsProviderSettings? Provider = null,
    ExtraTtsSettings? Extra = null);

/// <summary>普通引擎的不可变参数快照，连接与角色引用仍各自独立。</summary>
public sealed record ExtraTtsSettings(string Language = "zh", string Model = "", string Format = "wav",
    string ApiKey = "", double Rate = 1, double Pitch = 0)
{
    /// <summary>诊断字符串不得包含密钥。</summary>
    public override string ToString() => $"Language={Language}, Format={Format}, Rate={Rate}, Pitch={Pitch}, ApiKey=[redacted]";
}

/// <summary>TtsProtocolSettings：原神自动配音的业务类型。</summary>
public sealed record TtsProtocolSettings(string TextLanguage = "zh", string PromptLanguage = "zh",
    string MediaType = "wav", string TextSplitMethod = "cut5", bool StreamingMode = true, bool StreamPlayback = false);

/// <summary>TimeoutSettings：原神自动配音的业务类型。</summary>
public sealed record TimeoutSettings(int ConnectSeconds, int RequestSeconds, int SynthesisSeconds);

/// <summary>VoiceSettings：原神自动配音的业务类型。</summary>
public sealed record VoiceSettings(
    string Name, string GptWeightsPath, string SovitsWeightsPath,
    string ReferenceAudioPath, string PromptText, string? RequestedEmotion = null, string? ReferenceEmotion = null,
    bool ServerSidePaths = false, TtsProviderSettings? Provider = null);

/// <summary>OutputSettings：原神自动配音的业务类型。</summary>
public sealed record OutputSettings(string LogsDirectory, string AudioDirectory);

/// <summary>WindowSettings：原神自动配音的业务类型。</summary>
public sealed record WindowSettings(int Width, int Height);

/// <summary>ConfigurationLoadResult：原神自动配音的业务类型。</summary>
public sealed record ConfigurationLoadResult(AppSettings? Settings, string? Error)
{
    /// <summary>IsSuccess：当前业务对象的配置、身份或状态值。</summary>
    public bool IsSuccess => Settings is not null && Error is null;
}
