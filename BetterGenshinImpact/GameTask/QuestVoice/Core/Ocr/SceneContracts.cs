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

/// <summary>SceneSelectionMode：原神自动配音的业务类型。</summary>
public enum SceneSelectionMode { Explicit, Automatic }
/// <summary>SceneDecisionKind：原神自动配音的业务类型。</summary>
public enum SceneDecisionKind { Explicit, Selected, Rejected, Unknown }
/// <summary>SceneDecision：原神自动配音的业务类型。</summary>
public sealed record SceneDecision(SceneDecisionKind Kind, string? LayoutId, string Reason,
    string AlgorithmVersion, string? CatalogSha256, IReadOnlyDictionary<string, double> Evidence);
/// <summary>Selected catalog foreground thresholds; absent for arbitrary explicit templates.</summary>
public sealed record SpeakerInkPolicy(string Version, int MinRed, int MinGreen, int MinBlue, int MaxBlue,
    int MinRedGreen, int MinGreenBlue, int MinRedBlue, int MaxSpread);
/// <summary>SceneSelection：原神自动配音的业务类型。</summary>
public sealed record SceneSelection(SceneDecision Decision, SceneProfile? Profile, SpeakerInkPolicy? SpeakerInk = null);

/// <summary>ISceneSelector：原神自动配音的业务类型。</summary>
public interface ISceneSelector
{
    /// <summary>在后台判断当前画面是否满足受支持的原神对白布局。</summary>
    Task<SceneSelection> SelectAsync(ReadOnlyMemory<byte> image, SceneSettings settings, CancellationToken cancellationToken);
}
