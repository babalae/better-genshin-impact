// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;

/// <summary>Owns a copy. Neither the caller's buffer nor a reopened source file is used downstream.</summary>
public sealed class ContinuousFrameSnapshot
{
    /// <summary>实例拥有的编码图像副本。</summary>
    private readonly byte[] _bytes;
    /// <summary>Sequence：当前业务对象的配置、身份或状态值。</summary>
    public long Sequence { get; }
    /// <summary>ObservedAt：当前业务对象的配置、身份或状态值。</summary>
    public TimeSpan ObservedAt { get; }
    /// <summary>Sha256：当前业务对象的配置、身份或状态值。</summary>
    public string Sha256 { get; }
    /// <summary>宿主在同一帧正向识别主界面且排除对白 UI；仍需门禁多帧确认。</summary>
    public bool DialogueExitConfirmed { get; }
    /// <summary>Bytes：当前业务对象的配置、身份或状态值。</summary>
    public ReadOnlyMemory<byte> Bytes => new((byte[])_bytes.Clone());
    /// <summary>建立 ContinuousFrameSnapshot 的业务依赖与初始状态。</summary>
    public ContinuousFrameSnapshot(long sequence, TimeSpan observedAt, ReadOnlyMemory<byte> bytes, bool dialogueExitConfirmed = false)
    {
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (observedAt < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(observedAt));
        if (bytes.Length is <= 0 or > 100_000_000)
            throw new ArgumentException("Frame must contain 1..100000000 encoded bytes.", nameof(bytes));
        Sequence = sequence; ObservedAt = observedAt; _bytes = bytes.ToArray(); DialogueExitConfirmed = dialogueExitConfirmed;
        Sha256 = Convert.ToHexString(SHA256.HashData(_bytes)).ToLowerInvariant();
    }
}

/// <summary>Per-ROI grid ink counts; comparison uses occupied ink, not full-screen empty pixels.</summary>
public sealed class ContinuousFrameSignature(string identity, IEnumerable<IEnumerable<int>> regions)
{
    /// <summary>Identity：当前业务对象的配置、身份或状态值。</summary>
    public string Identity { get; } = identity;
    /// <summary>Regions：当前业务对象的配置、身份或状态值。</summary>
    public IReadOnlyList<IReadOnlyList<int>> Regions { get; } = Array.AsReadOnly(regions
        .Select(region => (IReadOnlyList<int>)Array.AsReadOnly(region.ToArray())).ToArray());
}

/// <summary>ContinuousFrameInspection：原神自动配音的业务类型。</summary>
public sealed record ContinuousFrameInspection(ImageSourceSnapshot Source, SceneSelection Selection,
    ContinuousFrameSignature? Signature);
/// <summary>ContinuousFrameEventKind：原神自动配音的业务类型。</summary>
public enum ContinuousFrameEventKind
{
    Started, Observed, Invalidated, LeftDialogue, OcrQueued, OcrStarted, OcrCompleted,
    CandidateAccepted, Discarded, Failed, Completing, Completed, Stopping
}
/// <summary>Elapsed is this session's monotonic time. Frame time is caller-observed input time.</summary>
public sealed record ContinuousFrameEvent(long Sequence, TimeSpan Elapsed, ContinuousFrameEventKind Kind,
    long Revision, long Epoch, long? SourceSequence, TimeSpan? ObservedAt, string? ImageSha256,
    string? Detail = null, ContinuousFrameInspection? Inspection = null, ImageOcrResult? Ocr = null, long SemanticGeneration = 0);
/// <summary>ContinuousFrameState：原神自动配音的业务类型。</summary>
public sealed record ContinuousFrameState(long Revision, long Epoch, bool Stable, bool Completed, bool Stopped, long SemanticGeneration = 0);
/// <summary>IContinuousFrameInspector：原神自动配音的业务类型。</summary>
public interface IContinuousFrameInspector
{
    /// <summary>读取场景并量测对白区域墨迹。</summary>
    Task<ContinuousFrameInspection> InspectAsync(ContinuousFrameSnapshot frame, CancellationToken cancellationToken);
}
/// <summary>IContinuousFrameRecognizer：原神自动配音的业务类型。</summary>
public interface IContinuousFrameRecognizer
{
    /// <summary>在取消检查和配置约束下执行识别。</summary>
    Task<ImageOcrResult> RecognizeAsync(ContinuousFrameSnapshot frame, ContinuousFrameInspection inspection,
        CancellationToken cancellationToken);
}
