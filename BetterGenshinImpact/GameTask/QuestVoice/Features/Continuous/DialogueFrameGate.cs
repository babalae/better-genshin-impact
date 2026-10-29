// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
/// <summary>FrameGateDecision：原神自动配音的业务类型。</summary>
public sealed record FrameGateDecision(bool Invalidate, bool Leave, bool Recognize, string Reason);
/// <summary>DialogueFrameGate：原神自动配音的业务类型。</summary>
public sealed class DialogueFrameGate
{
    /// <summary>当前实例采用的配置快照。</summary>
    private readonly ContinuousSettings _settings;
    /// <summary>用于稳定性比较的锚定墨迹签名。</summary>
    private ContinuousFrameSignature? _anchor;
    /// <summary>最近接受的单调帧序号。</summary>
    private long _lastSequence = -1;
    /// <summary>保存 _lastAt, _anchorAt, _absentAt, _lastOcrAt 对应的本轮状态或配置，不跨会话复用。</summary>
    private TimeSpan? _lastAt, _anchorAt, _absentAt, _lastOcrAt;
    /// <summary>当前锚定签名连续出现的帧数。</summary>
    private int _frames;
    /// <summary>标记本轮是否已经发出离开对白通知。</summary>
    private bool _left;
    /// <summary>主界面正向证据的连续起点与帧数；不把缺失对白当作退出证据。</summary>
    private TimeSpan? _exitAt;
    /// <summary>连续观察到主界面且未选中对白的次数。</summary>
    private int _exitFrames;
    /// <summary>持续不确定场景的有界宽限，超时原因与可信退出明确区分。</summary>
    private const int UncertainSceneTimeoutMilliseconds = 30_000;
    /// <summary>Stable：当前业务对象的配置、身份或状态值。</summary>
    public bool Stable { get; private set; }
    /// <summary>建立 DialogueFrameGate 的业务依赖与初始状态。</summary>
    public DialogueFrameGate(ContinuousSettings settings) { ContinuousSettingsLoader.Validate(settings); _settings = settings; }
    /// <summary>对单调到达的帧判断改变、稳定、复查与离开对白。</summary>
    public FrameGateDecision Observe(ContinuousFrameSnapshot frame, ContinuousFrameInspection inspection)
    {
        if (frame.Sequence <= _lastSequence || frame.ObservedAt <= _lastAt)
            throw new ArgumentException("Frame sequence and nonnegative observation times must strictly increase.");
        var gap = _lastAt is { } last && (frame.ObservedAt - last).TotalMilliseconds > _settings.MaximumObservationGapMilliseconds;
        _lastSequence = frame.Sequence; _lastAt = frame.ObservedAt;
        if (gap) { Reset(); _absentAt = null; }
        if (inspection.Selection.Decision.Kind != SceneDecisionKind.Selected || inspection.Signature is null)
        {
            var invalidate = _anchor is not null || gap;
            ResetStability(); _absentAt ??= frame.ObservedAt;
            if (frame.DialogueExitConfirmed)
            { _exitAt ??= frame.ObservedAt; _exitFrames++; }
            else { _exitAt = null; _exitFrames = 0; }
            var confirmed = frame.DialogueExitConfirmed && _exitFrames >= _settings.MinimumStableFrames
                && (frame.ObservedAt - _exitAt!.Value).TotalMilliseconds >= _settings.LeaveDialogueMilliseconds;
            var expired = (frame.ObservedAt - _absentAt.Value).TotalMilliseconds >= UncertainSceneTimeoutMilliseconds;
            var leave = !_left && (confirmed || expired);
            if (leave) _left = true;
            return new(invalidate || leave, leave, false, leave ? confirmed ? "ConfirmedMainUiExit" : "UncertainSceneTimeout"
                : gap ? "ObservationGap" : "NoSelectedDialogue");
        }
        _absentAt = null; _left = false; _exitAt = null; _exitFrames = 0;
        var changed = _anchor is null || _anchor.Identity != inspection.Signature.Identity
            || Difference(_anchor, inspection.Signature) > _settings.SignatureDifferenceThreshold;
        if (changed)
        {
            _anchor = inspection.Signature; _anchorAt = frame.ObservedAt; _frames = 1; _lastOcrAt = null; Stable = false;
        }
        else _frames++;
        Stable = _frames >= _settings.MinimumStableFrames && (frame.ObservedAt - _anchorAt!.Value).TotalMilliseconds >= _settings.StableMilliseconds;
        var recognize = Stable && (_lastOcrAt is null || (frame.ObservedAt - _lastOcrAt.Value).TotalMilliseconds >= _settings.RecheckMilliseconds);
        if (recognize) _lastOcrAt = frame.ObservedAt;
        return new(changed || gap, false, recognize, gap ? "ObservationGap" : changed ? "VisualChange" : recognize ? "StableOrRecheck" : "StabilizingOrUnchanged");
    }
    /// <summary>重置视觉锚点及稳定状态。</summary>
    public void Reset() { ResetStability(); _exitAt = null; _exitFrames = 0; }
    /// <summary>未选中时只重置文字稳定性，连续可信退出证据由Observe单独维护。</summary>
    private void ResetStability() { _anchor = null; _anchorAt = null; _frames = 0; _lastOcrAt = null; Stable = false; }
    /// <summary>按实际墨迹占用量计算签名差异。</summary>
    public static double Difference(ContinuousFrameSignature first, ContinuousFrameSignature second)
    {
        if (first.Identity != second.Identity || first.Regions.Count != second.Regions.Count) return 1;
        var maximum = 0d;
        for (var roi = 0; roi < first.Regions.Count; roi++)
        {
            var a = first.Regions[roi]; var b = second.Regions[roi];
            if (a.Count != b.Count) return 1;
            long changed = 0, ink = 0;
            for (var i = 0; i < a.Count; i++) { changed += Math.Abs((long)a[i] - b[i]); ink += Math.Max(a[i], b[i]); }
            maximum = Math.Max(maximum, ink == 0 ? 0 : (double)changed / ink);
        }
        return maximum;
    }
}
