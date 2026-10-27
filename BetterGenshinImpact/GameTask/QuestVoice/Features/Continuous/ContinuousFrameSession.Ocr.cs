// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using System.Text;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;

/// <summary>ContinuousFrameSession：原神自动配音的业务类型。</summary>
public sealed partial class ContinuousFrameSession
{
    /// <summary>处理最新稳定帧并丢弃过期版本的 OCR 结果。</summary>
    private async Task OcrPumpAsync()
    {
        while (true)
        {
            await _wake.WaitAsync().ConfigureAwait(false);
            OcrWork? work;
            CancellationTokenSource? cancellation;
            await _publication.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_stopped) return;
                work = _pending; _pending = null;
                if (work is null) { if (_inputEnded) return; else continue; }
                cancellation = _activeOcr = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _metrics?.Record(QuestVoiceMetric.OcrQueue, Stopwatch.GetTimestamp() - work.QueuedAt);
                EmitWork(ContinuousFrameEventKind.OcrStarted, work);
            }
            finally { _publication.Release(); }
            ImageOcrResult? result = null;
            Exception? error = null;
            var batchStart = Stopwatch.GetTimestamp();
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                result = await _recognizer.RecognizeAsync(work.Frame, work.Inspection, cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                error = exception;
                // 原有事件仅携带消息；诊断文件另保留异常链，不改变后续接受/取消判定。
                if (exception is not OperationCanceledException)
                    _logger.Warning(exception, "QuestVoice OCR batch failed seq={Sequence} revision={Revision} epoch={Epoch}",
                        work.Frame.Sequence, work.Revision, work.Epoch);
            }
            _metrics?.Record(QuestVoiceMetric.OcrBatch, Stopwatch.GetTimestamp() - batchStart);
            // 姓名变体先只读核对真实声线；等待时不占publication，下一帧仍可废弃本OCR。
            long? checkedGeneration = null;
            var sameRoute = false;
            ContinuousDialogueCandidate? routeCheck = null;
            await _publication.WaitAsync().ConfigureAwait(false);
            try
            {
                if (error is null && result is not null && _voice is not null && !_stopped
                    && work.Revision == _revision && work.Epoch == _epoch && !cancellation.IsCancellationRequested
                    && _accepted is { } previous)
                {
                    var incoming = ReadText(result);
                    if (!SameSpeaker(previous, incoming)
                        && DialogueTextComparison.Compare(previous.Text, incoming.Text) == DialogueTextRelation.Equivalent)
                    {
                        checkedGeneration = _semanticGeneration;
                        routeCheck = new(_revision, _epoch, work.Frame.Sequence, work.Frame.ObservedAt, work.Frame.Sha256,
                            work.Inspection.Selection.Decision.LayoutId!, incoming.Name, incoming.Status, result.Dialogue,
                            _semanticGeneration, _dialogueOccurrence);
                    }
                }
            }
            finally { _publication.Release(); }
            if (routeCheck is not null)
            {
                try { sameRoute = await _voice!.MatchesCurrentDialogueAsync(routeCheck, cancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* 最终版本检查负责丢弃，不能让过期预检接受新句。 */ }
                catch (Exception exception)
                { _logger.Warning(exception, "QuestVoice routing comparison failed; accepting current OCR through normal routing"); }
            }
            await _publication.WaitAsync().ConfigureAwait(false);
            try
            {
                var current = !_stopped && work.Revision == _revision && work.Epoch == _epoch && !cancellation.IsCancellationRequested && (checkedGeneration is null || checkedGeneration == _semanticGeneration);
                _activeOcr = null;
                EmitWork(ContinuousFrameEventKind.OcrCompleted, work, error?.Message, result);
                if (!current || _stopped) EmitWork(ContinuousFrameEventKind.Discarded, work, "StaleOrCancelledOcr", result);
                else if (error is not null)
                {
                    _confirmation = null; EmitWork(ContinuousFrameEventKind.Failed, work, error.Message);
                }
                else if (result is not null) Publish(work, result, sameRoute);
                UpdateSnapshot();
                if (_pending is not null || _inputEnded || _stopped) Pulse();
            }
            finally { cancellation.Dispose(); _publication.Release(); }
        }
    }
    /// <summary>当前视觉证据只在确认新内容时推进独立的语音代际。</summary>
    private void Publish(OcrWork work, ImageOcrResult result, bool sameRoute)
    {
        if (!result.Dialogue.EnumerateRunes().Any(Rune.IsLetterOrDigit))
        {
            _confirmation = null;
            EmitWork(ContinuousFrameEventKind.Discarded, work, "NoSpeakableText", result); return;
        }
        var name = string.IsNullOrWhiteSpace(result.Speaker) ? null : result.Speaker;
        var incoming = ReadText(result); var status = incoming.Status;
        if (sameRoute)
        {
            // 别名只更新姓名/状态，保留最初接受的原文；否则用缺句末标点的变体覆盖后，
            // 后续 A?/A! 的成对判断会被错误放宽为“一侧缺失”。
            _accepted = _accepted is { } previousAlias ? incoming with { Text = previousAlias.Text } : incoming;
            _dialogueAnchor = work.Inspection; _confirmation = null;
            EmitWork(ContinuousFrameEventKind.Discarded, work, "SameAcceptedRouteRecheck", result); return;
        }
        var sameSpeaker = _accepted is { } previous && SameSpeaker(previous, incoming);
        var relation = sameSpeaker ? DialogueTextComparison.Compare(_accepted!.Text, incoming.Text)
            : DialogueTextRelation.Different;
        if (sameSpeaker && relation == DialogueTextRelation.Equivalent)
        {
            _confirmation = null;
            EmitWork(ContinuousFrameEventKind.Discarded, work, "SameAcceptedRecheck", result); return;
        }
        var needsConfirmation = (sameSpeaker && relation == DialogueTextRelation.NeedsConfirmation)
            || (_accepted is null && DialogueTextComparison.RequiresInitialConfirmation(incoming.Text));
        if (needsConfirmation)
        {
            // 至少两个不同帧的OCR支持同一变体才接受；时间本身不是证据。
            var confirmed = _confirmation is { } confirmation && ConfirmationIsCurrent(confirmation)
                && work.Frame.Sequence != confirmation.SourceSequence && SameSpeaker(confirmation.Text, incoming)
                && DialogueTextComparison.CreateComparisonKey(confirmation.Text.Text)
                    == DialogueTextComparison.CreateComparisonKey(incoming.Text);
            if (!confirmed)
            {
                // 变体抖动不重置额外OCR预算，避免噪声造成密集重试风暴。
                var attempts = _confirmation is { } pending && ConfirmationIsCurrent(pending) ? pending.Attempts : 0;
                _confirmation = new(incoming, _revision, _epoch, _semanticGeneration, work.Frame.Sequence,
                    work.Frame.ObservedAt, attempts);
                EmitWork(ContinuousFrameEventKind.Discarded, work, "AwaitingTextConfirmation", result); return;
            }
        }
        _confirmation = null;
        if (_stopped) return;
        _accepted = incoming; _dialogueAnchor = work.Inspection; _clearCommitted = false;
        _semanticGeneration++;
        _voice?.Invalidate(_semanticGeneration);
        var candidate = new ContinuousDialogueCandidate(_revision, _epoch, work.Frame.Sequence, work.Frame.ObservedAt,
            work.Frame.Sha256, work.Inspection.Selection.Decision.LayoutId!, name, status, result.Dialogue,
            _semanticGeneration, _dialogueOccurrence);
        Emit(ContinuousFrameEventKind.CandidateAccepted, work.Frame, inspection: work.Inspection, ocr: result);
        if (!_stopped) _voice?.Offer(candidate);
    }

    /// <summary>原文与被接受的姓名状态分离；仅姓名遵循既有规范化规则。</summary>
    private static AcceptedText ReadText(ImageOcrResult result)
    {
        var name = string.IsNullOrWhiteSpace(result.Speaker) ? null : Normalize(result.Speaker);
        var status = name is not null ? SpeakerNameStatus.Accepted
            : result.Regions.Any(r => r.Region.Kind == OcrRegionKind.Speaker && r.RejectionReason == OcrRejectionReason.LowConfidence)
                ? SpeakerNameStatus.Rejected : SpeakerNameStatus.Missing;
        return new(name, status, result.Dialogue);
    }
    /// <summary>姓名状态必须一致；不把拒绝姓名当作可信角色身份。</summary>
    private static bool SameSpeaker(AcceptedText first, AcceptedText second) => first.Status == second.Status
        && StringComparer.OrdinalIgnoreCase.Equals(first.Name, second.Name);

    /// <summary>确认只属于当前视觉版本、epoch和语音代际。</summary>
    private bool ConfirmationIsCurrent(TextConfirmation confirmation) => confirmation.Revision == _revision
        && confirmation.Epoch == _epoch && confirmation.Generation == _semanticGeneration;

    /// <summary>只在下一张真实稳定帧按需安排最多两次额外OCR；周期2秒不变。</summary>
    private bool ShouldConfirm(ContinuousFrameSnapshot frame, FrameGateDecision decision) => !decision.Recognize
        && _gate.Stable && _activeOcr is null && _pending is null && _confirmation is { Attempts: < 2 } confirmation
        && ConfirmationIsCurrent(confirmation) && frame.Sequence != confirmation.SourceSequence
        && frame.ObservedAt - confirmation.LastRequestedAt >= TimeSpan.FromMilliseconds(200);

    /// <summary>姓名和分隔线仍可信、正文完全无墨迹的连续帧才构成真实清空证据。</summary>
    private void ObserveDialogueClear(ContinuousFrameSnapshot frame, ContinuousFrameInspection inspection, FrameGateDecision decision)
    {
        if (decision.Reason == "ObservationGap" || !HasDialogueClearEvidence(inspection))
        { _clearAt = null; _clearFrames = 0; return; }
        if (_clearCommitted) return;
        _clearAt ??= frame.ObservedAt; _clearFrames++;
        if (_clearFrames < 2 || frame.ObservedAt - _clearAt.Value < TimeSpan.FromMilliseconds(200)) return;
        _clearCommitted = true; _accepted = null; _confirmation = null; _dialogueOccurrence++;
        // 清空不能让旧Ready稍后入播；已经播放的旧句仍按Ready后接替规则结束。
        _semanticGeneration++;
        _voice?.Invalidate(_semanticGeneration);
        Emit(ContinuousFrameEventKind.Invalidated, frame, "ConfirmedDialogueClear", inspection);
    }

    /// <summary>复用最近Selected的同目录证据作保守锚，不复制场景算法或其可配置阈值。</summary>
    private bool HasDialogueClearEvidence(ContinuousFrameInspection inspection)
    {
        if (_dialogueAnchor is not { } anchor || inspection.Selection.Decision is not
            { Kind: SceneDecisionKind.Rejected, Reason: "NoSupportedDialogueFrame" } decision
            || decision.CatalogSha256 != anchor.Selection.Decision.CatalogSha256
            || decision.AlgorithmVersion != anchor.Selection.Decision.AlgorithmVersion
            || inspection.Source.Width != anchor.Source.Width || inspection.Source.Height != anchor.Source.Height) return false;
        var evidence = decision.Evidence; var accepted = anchor.Selection.Decision.Evidence;
        bool AtLeastAnchor(string key) => accepted.TryGetValue(key, out var minimum) && minimum > 0
            && evidence.TryGetValue(key, out var value) && value >= minimum;
        // Selected锚保证恰好一个separator合格。两种Rows都原样保留，才能证明该唯一合格者仍在；
        // 不能把另一个超过MaximumThickness但Rows>0的噪声分隔线误作新正向证据。
        bool SameRows(string prefix) => evidence.GetValueOrDefault(prefix + ".Rows")
            == accepted.GetValueOrDefault(prefix + ".Rows");
        bool SamePositiveRows(string prefix) => accepted.TryGetValue(prefix + ".Rows", out var rows) && rows > 0
            && evidence.TryGetValue(prefix + ".Rows", out var currentRows) && currentRows == rows
            && AtLeastAnchor(prefix + ".SideRun");
        return evidence.TryGetValue("TextPixels", out var pixels) && pixels == 0
            && evidence.TryGetValue("TextComponents", out var components) && components == 0
            && AtLeastAnchor("NamePixels") && AtLeastAnchor("NameTallComponents")
            && SameRows("StandardSeparator") && SameRows("TitleSeparator")
            && (SamePositiveRows("StandardSeparator") || SamePositiveRows("TitleSeparator"));
    }
    /// <summary>执行规范化与边缘空白处理，不作模糊角色猜测。</summary>
    private static string? Normalize(string? text) => text?.Trim().Normalize(NormalizationForm.FormC);
    /// <summary>为拥有的 OCR 工作附带版本信息后发布事件。</summary>
    private void EmitWork(ContinuousFrameEventKind kind, OcrWork work, string? detail = null, ImageOcrResult? ocr = null) =>
        Emit(kind, work.Frame, detail, work.Inspection, ocr, work.Revision, work.Epoch);
    /// <summary>排队投递有序诊断事件，并隔离订阅者异常。</summary>
    private void Emit(ContinuousFrameEventKind kind, ContinuousFrameSnapshot? frame = null, string? detail = null,
        ContinuousFrameInspection? inspection = null, ImageOcrResult? ocr = null, long? revision = null, long? epoch = null)
    {
        lock (_notificationsGate)
        {
            var value = new ContinuousFrameEvent(++_eventSequence, _clock.Elapsed, kind,
                revision ?? _revision, epoch ?? _epoch, frame?.Sequence, frame?.ObservedAt, frame?.Sha256, detail, inspection, ocr, _semanticGeneration);
            _notifications.Enqueue(value);
            if (_notifying) return;
            _notifying = true;
            try
            {
                while (_notifications.TryDequeue(out var next))
                {
                    // 宿主主日志不展开每帧的万级墨迹网格；保留版本、正式文字和故障诊断。
                    if (next.Kind != ContinuousFrameEventKind.Observed)
                        _logger.Debug("QuestVoice frame {Kind} seq={Sequence} revision={Revision} epoch={Epoch} speaker={Speaker} text={Text} detail={Detail}",
                            next.Kind, next.SourceSequence, next.Revision, next.Epoch, next.Ocr?.Speaker, next.Ocr?.Dialogue, next.Detail);
                    if (Event is not { } handlers) continue;
                    foreach (Action<ContinuousFrameEvent> handler in handlers.GetInvocationList())
                        try { handler(next); } catch (Exception exception) { _logger.Error(exception, "Continuous frame subscriber failed"); }
                }
            }
            finally { _notifying = false; }
        }
    }
}
