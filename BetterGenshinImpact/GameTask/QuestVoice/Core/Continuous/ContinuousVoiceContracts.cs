// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;

/// <summary>ContinuousDialogueCandidate：原神自动配音的业务类型。</summary>
public sealed record ContinuousDialogueCandidate(long Revision, long Epoch, long SourceSequence,
    TimeSpan ObservedAt, string ImageSha256, string LayoutId, string? AcceptedName,
    SpeakerNameStatus NameStatus, string Dialogue, long? SemanticGeneration = null, long DialogueOccurrence = 0)
{
    /// <summary>语音所有权的语义代际；旧直接调用以Revision兼容。</summary>
    public long Generation => SemanticGeneration ?? Revision;
}

/// <summary>ContinuousVoiceWork：原神自动配音的业务类型。</summary>
public sealed record ContinuousVoiceWork(ContinuousDialogueCandidate Candidate, ContinuousVoiceIdentity? Identity = null,
    VoiceRoutingResult? Routing = null, string? ArtifactPath = null);

/// <summary>ContinuousVoiceSnapshot：原神自动配音的业务类型。</summary>
public sealed record ContinuousVoiceSnapshot(bool Started, bool Accepting, bool Stopping, bool Completed,
    long Revision, long Epoch, ContinuousVoiceWork? Pending, ContinuousVoiceWork? Synthesizing,
    ContinuousVoiceWork? Ready, ContinuousVoiceWork? Playing, bool BackendUnknown,
    VoiceErrorCode? Error, string? ErrorDetail, int ObservationCount);

/// <summary>A single-use session. Cancellation of a wait does not abandon owned work.</summary>
public interface IContinuousVoiceSession : IAsyncDisposable
{
    /// <summary>读取当前不可变状态快照。</summary>
    ContinuousVoiceSnapshot Snapshot { get; }
    /// <summary>Ordered synchronous notifications. Reentrant notifications queue behind the current
    /// notification. Handlers may call lifecycle methods and asynchronously await their returned tasks.</summary>
    event Action<ContinuousVoiceEvent>? Event;
    /// <summary>Starts the single-use session at Revision=0, Epoch=0. The token cancels this start only.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);
    /// <summary>Requires a greater semantic generation. Discards only unplayed work; current playback continues.</summary>
    void Invalidate(long revision);
    /// <summary>Accepts at most one candidate per current semantic generation and epoch; stale/repeated offers are discarded.</summary>
    void Offer(ContinuousDialogueCandidate candidate);
    /// <summary>只读解析姓名与实际声线路由，不发HTTP；调用方拥有等待并在返回后复查帧版本。
    /// 返回true时当前语义已有同句所有权，候选仍保留最初接受的图像来源。</summary>
    Task<bool> MatchesCurrentDialogueAsync(ContinuousDialogueCandidate candidate, CancellationToken cancellationToken = default);
    /// <summary>Increments Epoch, invalidates work, stops playback and waits for its actual exit.
    /// Issued synthesis drains in the background and remains owned until Complete/Stop/Dispose.</summary>
    Task LeaveDialogueAsync(long revision);
    /// <summary>Hold before inspecting new visual evidence; release after revision decisions.</summary>
    IDisposable BeginObservation();
    /// <summary>Closes input, drains the latest valid candidate, and waits for natural playback end.</summary>
    Task CompleteAsync(CancellationToken cancellationToken = default);
    /// <summary>Closes input, requests immediate playback stop, and drains both pumps. The token
    /// cancels only the caller's wait; Dispose still waits for actual completion.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
