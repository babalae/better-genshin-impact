using System;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;

/// <summary>
/// 跟踪对白周期（epoch）、视觉版本（revision）和语音代际（semanticGeneration）的单调推进。
/// 页面状态投影和悬浮摘要共用这一份过期判定，不各自重复实现。非线程安全，由调用方加锁。
/// </summary>
public sealed class DialogueVersionTracker
{
    /// <summary>当前对白周期。</summary>
    public long Epoch { get; private set; }
    /// <summary>当前视觉版本。</summary>
    public long Revision { get; private set; }
    /// <summary>当前语音代际；只在进入新身份时推进。</summary>
    public long SemanticGeneration { get; private set; }

    /// <summary>事件是否早于当前已推进的周期和视觉版本。</summary>
    public bool IsStale(long epoch, long revision) => epoch < Epoch || epoch == Epoch && revision < Revision;

    /// <summary>
    /// 推进周期和视觉版本；过期事件返回 false。
    /// 周期变化或语音代际增大时 <paramref name="newIdentity"/> 为 true，调用方据此清理上一身份的候选标记。
    /// </summary>
    public bool TryAdvance(long epoch, long revision, long semanticGeneration, out bool newIdentity)
    {
        newIdentity = false;
        if (IsStale(epoch, revision)) return false;
        newIdentity = epoch != Epoch || semanticGeneration > SemanticGeneration;
        if (newIdentity) SemanticGeneration = semanticGeneration;
        Epoch = epoch;
        Revision = revision;
        return true;
    }

    /// <summary>离开对白时推进周期；视觉版本可能未知，语音代际只增不减。</summary>
    public bool TryLeave(long epoch, long? revision, long semanticGeneration)
    {
        if (epoch < Epoch) return false;
        Epoch = epoch;
        if (revision is { } visualRevision) Revision = visualRevision;
        SemanticGeneration = Math.Max(SemanticGeneration, semanticGeneration);
        return true;
    }

    /// <summary>新会话从零开始。</summary>
    public void Reset()
    {
        Epoch = 0;
        Revision = 0;
        SemanticGeneration = 0;
    }
}
