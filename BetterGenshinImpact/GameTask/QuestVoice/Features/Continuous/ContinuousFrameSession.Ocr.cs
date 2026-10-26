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
            await _publication.WaitAsync().ConfigureAwait(false);
            try
            {
                var current = !_stopped && work.Revision == _revision && work.Epoch == _epoch && !cancellation.IsCancellationRequested;
                _activeOcr = null;
                EmitWork(ContinuousFrameEventKind.OcrCompleted, work, error?.Message, result);
                if (!current || _stopped) EmitWork(ContinuousFrameEventKind.Discarded, work, "StaleOrCancelledOcr", result);
                else if (error is not null)
                {
                    FailVisual(work.Frame, "OcrFailed"); EmitWork(ContinuousFrameEventKind.Failed, work, error.Message);
                }
                else if (result is not null) Publish(work, result);
                UpdateSnapshot();
                if (_pending is not null || _inputEnded || _stopped) Pulse();
            }
            finally { cancellation.Dispose(); _publication.Release(); }
        }
    }
    /// <summary>只对当前视觉版本接受文字并提交候选，避免旧句重放。</summary>
    private void Publish(OcrWork work, ImageOcrResult result)
    {
        if (!result.Dialogue.EnumerateRunes().Any(Rune.IsLetterOrDigit))
        {
            _emptyResultObserved = true;
            FailVisual(work.Frame, "NoSpeakableText");
            EmitWork(ContinuousFrameEventKind.Discarded, work, "NoSpeakableText", result); return;
        }
        var name = string.IsNullOrWhiteSpace(result.Speaker) ? null : result.Speaker;
        var status = name is not null ? SpeakerNameStatus.Accepted
            : result.Regions.Any(r => r.Region.Kind == OcrRegionKind.Speaker && r.RejectionReason == OcrRejectionReason.LowConfidence)
                ? SpeakerNameStatus.Rejected : SpeakerNameStatus.Missing;
        var accepted = new AcceptedText(Normalize(name), status, Normalize(result.Dialogue)!);
        var same = _accepted is { } previous && previous.Status == accepted.Status
            && StringComparer.OrdinalIgnoreCase.Equals(previous.Name, accepted.Name) && previous.Text == accepted.Text;
        // Continue recording periodic OCR after a failure, but it cannot manufacture a retry of the
        // same accepted candidate. A newly confirmed text identity is new evidence and may proceed.
        if (_visualFailed && (same || (_accepted is null && !_emptyResultObserved)))
        { EmitWork(ContinuousFrameEventKind.Discarded, work, "FailedVisualVersion", result); return; }
        if (_visualFailed) { _visualFailed = false; Invalidate(work.Frame, "OcrDetectedChange"); }
        // Rechecks are observational. Only a changed accepted result creates a revision.
        if (_offeredRevision == _revision && same) { EmitWork(ContinuousFrameEventKind.Discarded, work, "SameAcceptedRecheck", result); return; }
        if (_offeredRevision == _revision && !same) Invalidate(work.Frame, "OcrDetectedChange");
        if (_stopped) return;
        _accepted = accepted; _offeredRevision = _revision;
        var candidate = new ContinuousDialogueCandidate(_revision, _epoch, work.Frame.Sequence, work.Frame.ObservedAt,
            work.Frame.Sha256, work.Inspection.Selection.Decision.LayoutId!, name, status, result.Dialogue);
        Emit(ContinuousFrameEventKind.CandidateAccepted, work.Frame, inspection: work.Inspection, ocr: result);
        if (!_stopped) _voice?.Offer(candidate);
    }
    /// <summary>将当前视觉版本标记为识别失败，等待新证据恢复。</summary>
    private void FailVisual(ContinuousFrameSnapshot frame, string reason)
    {
        if (_visualFailed) return;
        _visualFailed = true;
        Invalidate(frame, reason);
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
                revision ?? _revision, epoch ?? _epoch, frame?.Sequence, frame?.ObservedAt, frame?.Sha256, detail, inspection, ocr);
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
