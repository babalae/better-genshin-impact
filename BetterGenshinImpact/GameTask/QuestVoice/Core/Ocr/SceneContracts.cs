// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

/// <summary>SceneDecisionKind：原神自动配音的业务类型。</summary>
public enum SceneDecisionKind { Selected, Rejected, Unknown }
/// <summary>SceneDecision：原神自动配音的业务类型。</summary>
public sealed record SceneDecision(SceneDecisionKind Kind, string? LayoutId, string Reason,
    string AlgorithmVersion, string? CatalogSha256, IReadOnlyDictionary<string, double> Evidence);
/// <summary>Selected catalog foreground thresholds; absent for arbitrary explicit templates.</summary>
public sealed record SpeakerInkPolicy(string Version, int MinRed, int MinGreen, int MinBlue, int MaxBlue,
    int MinRedGreen, int MinGreenBlue, int MinRedBlue, int MaxSpread);
/// <summary>SceneSelection：原神自动配音的业务类型。</summary>
public sealed record SceneSelection(SceneDecision Decision, SceneProfile? Profile, SpeakerInkPolicy? SpeakerInk = null);

