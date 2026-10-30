using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

/// <summary>把核心事件投影为页面状态快照；只读事件，不参与 OCR、候选接受或音频调度。</summary>
public sealed partial class QuestVoiceService
{
    /// <summary>最近事件列表最大保留条数。</summary>
    private const int MaxRecentEvents = 8;
    /// <summary>保护显示快照与本轮进度状态。</summary>
    private readonly object _statusGate = new();
    /// <summary>有限诊断行，避免独立复制日志系统。</summary>
    private readonly Queue<string> _diagnostics = new();
    /// <summary>与悬浮摘要共用的周期、视觉版本与语音代际判定，区分新旧对白轮次。</summary>
    private readonly DialogueVersionTracker _versions = new();
    /// <summary>当前正式接受的候选身份（Epoch, Revision, SourceSequence）。</summary>
    private (long Epoch, long Revision, long Source)? _acceptedCandidate;
    /// <summary>当前正在播放的候选身份（Epoch, Revision, SourceSequence）。</summary>
    private (long Epoch, long Revision, long Source)? _playingCandidate;
    /// <summary>当前是否处于对白画面及是否有音频正在输出。</summary>
    private bool _inDialogue, _isPlayingAudio;
    /// <summary>最近一次记录的界面事件内容，避免稳定画面重复刷屏。</summary>
    private string? _lastEventText;
    /// <summary>线程间只发布完整快照。</summary>
    private QuestVoiceStatus _status = new();

    /// <summary>读取完整的真实状态快照。</summary>
    public QuestVoiceStatus Status { get { lock (_statusGate) return _status; } }
    /// <summary>回调可能来自工作线程，订阅方必须异步投递 UI。</summary>
    public event Action<QuestVoiceStatus>? Changed;

    /// <summary>新会话开始前清空上一轮的版本与候选标记。</summary>
    private void ResetProjection()
    {
        lock (_statusGate)
        {
            _versions.Reset();
            _acceptedCandidate = null;
            _playingCandidate = null;
            _inDialogue = false;
            _isPlayingAudio = false;
            _lastEventText = null;
        }
    }
    /// <summary>OCR 状态来自实际事件，正文只显示正式接受的候选。</summary>
    private void OnFrameEvent(ContinuousFrameEvent value)
    {
        _overlay.OnFrame(value);
        switch (value.Kind)
        {
            case ContinuousFrameEventKind.Started:
                Update(s => ResetStages(s) with
                {
                    OverallStatus = "等待对白",
                    OverallStatusState = QuestVoiceStageState.InProgress,
                    DialogueState = "等待对白",
                    StageSummary = "当前阶段：等待检测到原神对话界面"
                });
                break;

            case ContinuousFrameEventKind.Observed when value.Inspection is { } inspection:
                var layoutText = inspection.Selection.Decision.LayoutId ?? inspection.Selection.Decision.Reason;
                var isSelected = inspection.Selection.Decision.Kind == SceneDecisionKind.Selected;
                var enteredDialogue = false;
                var hasAcceptedCurrent = false;
                lock (_statusGate)
                {
                    if (!AdvanceVersion(value.Epoch, value.Revision, value.SemanticGeneration)) return;
                    if (isSelected && !_inDialogue)
                    {
                        _inDialogue = true;
                        enteredDialogue = true;
                    }
                    hasAcceptedCurrent = _acceptedCandidate is { } acc
                        && acc.Epoch == value.Epoch;
                }
                if (enteredDialogue) RecordEvent("检测到对话界面");
                Update(s =>
                {
                    var next = s with { Layout = layoutText };
                    if (isSelected && !hasAcceptedCurrent && next.OcrStage == QuestVoiceStageState.Pending)
                    {
                        next = next with
                        {
                            OverallStatus = string.IsNullOrEmpty(next.Error) ? "运行中" : "异常",
                            OverallStatusState = string.IsNullOrEmpty(next.Error) ? QuestVoiceStageState.Completed : QuestVoiceStageState.Failed,
                            SceneStage = QuestVoiceStageState.Completed,
                            DialogueState = "已检测到对话界面（等待对白稳定）",
                            StageSummary = "当前阶段：已检测到对话界面，等待对白稳定"
                        };
                    }
                    return next;
                });
                break;

            case ContinuousFrameEventKind.Invalidated:
                var inDialogueNow = false;
                var playingNow = false;
                lock (_statusGate)
                {
                    if (!AdvanceVersion(value.Epoch, value.Revision, value.SemanticGeneration)) return;
                    // 普通画面变化仅重做观察/OCR，不回退正在合成或播放的语音阶段。
                    if (_acceptedCandidate is not null) break;
                    inDialogueNow = _inDialogue;
                    playingNow = _isPlayingAudio;
                }
                Update(s => s with
                {
                    SceneStage = inDialogueNow ? QuestVoiceStageState.Completed : QuestVoiceStageState.Pending,
                    OcrStage = QuestVoiceStageState.Pending,
                    SpeakerStage = QuestVoiceStageState.Pending,
                    RoutingStage = QuestVoiceStageState.Pending,
                    TtsStage = QuestVoiceStageState.Pending,
                    PlaybackStage = playingNow ? QuestVoiceStageState.InProgress : QuestVoiceStageState.Pending,
                    DialogueState = inDialogueNow ? "对白更新，等待文字稳定" : "等待对白",
                    StageSummary = inDialogueNow ? "当前阶段：检测到对白变化，等待文字稳定" : "当前阶段：等待检测到原神对话界面"
                });
                break;

            case ContinuousFrameEventKind.OcrStarted:
                var skipRecheck = false;
                var playingDuringOcr = false;
                lock (_statusGate)
                {
                    if (!AdvanceVersion(value.Epoch, value.Revision, value.SemanticGeneration)) return;
                    _inDialogue = true;
                    playingDuringOcr = _isPlayingAudio;
                    // 已接受语音身份跨视觉重检保持有效，不回退流程节点。
                    skipRecheck = _acceptedCandidate is { } acc
                        && acc.Epoch == value.Epoch;
                }
                if (!skipRecheck)
                {
                    Update(s => s with
                    {
                        OverallStatus = string.IsNullOrEmpty(s.Error) ? "运行中" : "异常",
                        OverallStatusState = string.IsNullOrEmpty(s.Error) ? QuestVoiceStageState.Completed : QuestVoiceStageState.Failed,
                        SceneStage = QuestVoiceStageState.Completed,
                        OcrStage = QuestVoiceStageState.InProgress,
                        SpeakerStage = QuestVoiceStageState.Pending,
                        RoutingStage = QuestVoiceStageState.Pending,
                        TtsStage = QuestVoiceStageState.Pending,
                        PlaybackStage = playingDuringOcr ? QuestVoiceStageState.InProgress : QuestVoiceStageState.Pending,
                        Ocr = "识别中",
                        DialogueState = "正在识别对白",
                        StageSummary = "当前阶段：正在进行 OCR 识别"
                    });
                }
                break;

            case ContinuousFrameEventKind.OcrCompleted:
                var isAcceptedRevision = false;
                lock (_statusGate)
                {
                    if (IsStale(value.Epoch, value.Revision)) return;
                    isAcceptedRevision = _acceptedCandidate is { } acc
                        && acc.Epoch == value.Epoch;
                }
                if (value.Detail is not null)
                {
                    Update(s => s with
                    {
                        Ocr = "识别失败：" + value.Detail,
                        OcrStage = QuestVoiceStageState.Failed,
                        DialogueState = "OCR 识别异常",
                        StageSummary = "当前阶段：OCR 识别失败"
                    });
                }
                else if (!isAcceptedRevision)
                {
                    Update(s => s with { Ocr = "识别已返回" });
                }
                break;

            case ContinuousFrameEventKind.CandidateAccepted when value.Ocr is { } ocr:
                var sourceSeq = value.SourceSequence ?? -1;
                var playingWhenAccepted = false;
                lock (_statusGate)
                {
                    if (!AdvanceVersion(value.Epoch, value.Revision, value.SemanticGeneration)) return;
                    var identity = (value.Epoch, value.Revision, sourceSeq);
                    if (_acceptedCandidate == identity) return;
                    _acceptedCandidate = identity;
                    _inDialogue = true;
                    playingWhenAccepted = _isPlayingAudio;
                }
                var speakerName = string.IsNullOrWhiteSpace(ocr.Speaker) ? "未识别姓名" : ocr.Speaker.Trim();
                Update(s => s with
                {
                    OverallStatus = "运行中",
                    OverallStatusState = QuestVoiceStageState.Completed,
                    SceneStage = QuestVoiceStageState.Completed,
                    OcrStage = QuestVoiceStageState.Completed,
                    SpeakerStage = QuestVoiceStageState.Completed,
                    RoutingStage = QuestVoiceStageState.InProgress,
                    TtsStage = QuestVoiceStageState.Pending,
                    PlaybackStage = playingWhenAccepted ? QuestVoiceStageState.InProgress : QuestVoiceStageState.Pending,
                    DialogueState = "已检测到稳定对白",
                    Ocr = "已接受稳定对白",
                    Character = speakerName,
                    Dialogue = ocr.Dialogue,
                    Voice = "正在匹配声线...",
                    StageSummary = "当前阶段：OCR 与角色识别完成，正在匹配声线路由",
                    Error = ""
                });
                RecordEvent("OCR 识别成功");
                RecordEvent($"识别角色：{speakerName}");
                break;

            case ContinuousFrameEventKind.LeftDialogue:
                HandleLeftDialogue(value.Epoch, value.Revision, value.SemanticGeneration);
                break;

            case ContinuousFrameEventKind.Failed:
                Update(s => s with
                {
                    OverallStatus = "异常",
                    OverallStatusState = QuestVoiceStageState.Failed,
                    SceneStage = s.OcrStage == QuestVoiceStageState.InProgress ? s.SceneStage : QuestVoiceStageState.Failed,
                    OcrStage = s.OcrStage == QuestVoiceStageState.InProgress ? QuestVoiceStageState.Failed : s.OcrStage,
                    DialogueState = "识别异常",
                    StageSummary = "当前阶段：画面或 OCR 识别失败"
                });
                Diagnose(value.Detail ?? "帧处理失败");
                break;
        }
    }

    /// <summary>合成和播放独立显示，合成下一句不会清掉旧句播放状态。</summary>
    private void OnVoiceEvent(ContinuousVoiceEvent value)
    {
        _overlay.OnVoice(value);
        if (value.Kind == ContinuousVoiceEventKind.LeftDialogue)
        {
            HandleLeftDialogue(value.Epoch, null, value.Revision);
            return;
        }

        var candidate = value.Work?.Candidate;
        var candidateId = candidate is null
            ? ((long Epoch, long Revision, long Source)?)null
            : (candidate.Epoch, candidate.Revision, candidate.SourceSequence);

        if (value.Kind == ContinuousVoiceEventKind.PlaybackCompleted)
        {
            var isCurrentAccepted = false;
            lock (_statusGate)
            {
                if (candidateId is not null && _playingCandidate == candidateId)
                {
                    _isPlayingAudio = false;
                    _playingCandidate = null;
                }
                isCurrentAccepted = candidateId is not null && _acceptedCandidate == candidateId;
            }
            if (value.Error is not null)
            {
                Update(s => s with
                {
                    OverallStatus = "异常",
                    OverallStatusState = QuestVoiceStageState.Failed,
                    PlaybackStage = QuestVoiceStageState.Failed,
                    Playback = "播放失败",
                    StageSummary = "当前阶段：音频播放异常",
                    Error = value.Detail ?? "播放设备错误"
                });
                Diagnose(value.Detail ?? "播放设备错误");
            }
            else if (isCurrentAccepted)
            {
                Update(s => s with
                {
                    PlaybackStage = value.PlaybackSucceeded ? QuestVoiceStageState.Completed : QuestVoiceStageState.Pending,
                    Playback = value.PlaybackSucceeded ? "播放结束" : "已停止",
                    StageSummary = value.PlaybackSucceeded ? "当前阶段：本句配音播放完成，等待下一句对白" : s.StageSummary
                });
            }
            else
            {
                // 旧句播放结束时，若新句尚未开始播放，则将播放状态归位为等待播放音频。
                Update(s => s.PlaybackStage == QuestVoiceStageState.InProgress
                    ? s with { PlaybackStage = QuestVoiceStageState.Pending, Playback = "等待播放音频" }
                    : s);
            }
            return;
        }

        if (value.Kind == ContinuousVoiceEventKind.PlaybackStopFailed)
        {
            Update(s => s with
            {
                OverallStatus = "异常",
                OverallStatusState = QuestVoiceStageState.Failed,
                PlaybackStage = QuestVoiceStageState.Failed,
                Playback = "播放失败",
                StageSummary = "当前阶段：音频播放停止异常",
                Error = value.Detail ?? "播放设备错误"
            });
            Diagnose(value.Detail ?? "播放设备错误");
            return;
        }

        bool isCurrentCandidate;
        bool streamingMode;
        bool routingMode;
        bool playingAudio;
        lock (_statusGate)
        {
            isCurrentCandidate = candidateId is not null && _acceptedCandidate == candidateId;
            streamingMode = _activeStreamingEnabled;
            routingMode = _activeRoutingEnabled;
            playingAudio = _isPlayingAudio;
        }
        if (!isCurrentCandidate) return;

        switch (value.Kind)
        {
            case ContinuousVoiceEventKind.RoutingStarted:
                Update(s => s with
                {
                    RoutingStage = QuestVoiceStageState.InProgress,
                    StageSummary = "当前阶段：正在解析角色声线路由"
                });
                break;

            case ContinuousVoiceEventKind.Routed:
                var routing = value.Work?.Routing;
                var characterName = routing?.CanonicalName ?? value.Work?.Candidate.AcceptedName ?? "默认 NPC";
                var voiceDisplay = FormatRoutedVoice(routing);
                var voiceShortName = routing?.Voice.Name ?? _activeVoiceName;
                Update(s => s with
                {
                    Character = characterName,
                    Voice = voiceDisplay,
                    SpeakerStage = QuestVoiceStageState.Completed,
                    RoutingStage = QuestVoiceStageState.Completed,
                    StageSummary = $"当前阶段：声线路由完成（{voiceDisplay}）"
                });
                RecordEvent($"声线路由命中：{voiceShortName}");
                break;

            case ContinuousVoiceEventKind.Duplicate:
                Update(s => s with
                {
                    RoutingStage = QuestVoiceStageState.Completed,
                    TtsStage = QuestVoiceStageState.Completed,
                    PlaybackStage = QuestVoiceStageState.Completed,
                    Tts = "同句已播，跳过重复合成",
                    StageSummary = "当前阶段：当前对白已播放，等待下一句对白"
                });
                break;

            case ContinuousVoiceEventKind.SynthesisStarted:
                Update(s => s with
                {
                    RoutingStage = QuestVoiceStageState.Completed,
                    TtsStage = QuestVoiceStageState.InProgress,
                    Tts = streamingMode ? "流式合成中" : "合成中",
                    Playback = playingAudio ? s.Playback : "等待播放音频",
                    StageSummary = streamingMode
                        ? "当前阶段：正在等待流式音频首段返回"
                        : $"当前阶段：正在请求 {_activeProviderName} 合成语音"
                });
                RecordEvent($"开始 {_activeProviderName} 合成");
                break;

            case ContinuousVoiceEventKind.SynthesisDraining:
                Update(s => s with
                {
                    TtsStage = QuestVoiceStageState.InProgress,
                    Tts = "候选已取消，正在排空旧响应",
                    StageSummary = "当前阶段：正在排空上一条后端合成响应"
                });
                break;

            case ContinuousVoiceEventKind.AudioReady:
                var isStreamingReady = value.Detail is not null;
                Update(s => s with
                {
                    // 流式首段就绪时后续音频仍在接收，因此保持 TTS 合成进行中，允许与播放重叠。
                    TtsStage = isStreamingReady ? QuestVoiceStageState.InProgress : QuestVoiceStageState.Completed,
                    Tts = value.Detail ?? "完整音频已就绪",
                    StageSummary = isStreamingReady
                        ? "当前阶段：流式音频首段已就绪，准备播放并继续接收后续音频"
                        : "当前阶段：语音合成完成，等待开始播放"
                });
                if (isStreamingReady) RecordEvent("等待流式音频片段");
                break;

            case ContinuousVoiceEventKind.SynthesisCompleted:
                Update(s => s with
                {
                    TtsStage = QuestVoiceStageState.Completed,
                    Tts = value.Detail ?? "合成完成",
                    StageSummary = s.PlaybackStage == QuestVoiceStageState.InProgress
                        ? "当前阶段：语音合成已完成，正在播放音频"
                        : "当前阶段：语音合成已完成，等待播放音频"
                });
                break;

            case ContinuousVoiceEventKind.PlaybackStarted:
                lock (_statusGate)
                {
                    _isPlayingAudio = true;
                    _playingCandidate = candidateId;
                }
                Update(s => s with
                {
                    PlaybackStage = QuestVoiceStageState.InProgress,
                    Playback = "正在播放",
                    StageSummary = s.TtsStage == QuestVoiceStageState.InProgress
                        ? "当前阶段：流式合成与音频播放同时进行中"
                        : "当前阶段：正在播放音频"
                });
                RecordEvent("开始播放音频");
                break;

            case ContinuousVoiceEventKind.CandidateFailed:
                var unknown = value.BackendUnknown;
                var failedDuringRouting = value.Work?.Routing is null && routingMode;
                Update(s => s with
                {
                    OverallStatus = "异常",
                    OverallStatusState = QuestVoiceStageState.Failed,
                    RoutingStage = failedDuringRouting ? QuestVoiceStageState.Failed : s.RoutingStage,
                    TtsStage = failedDuringRouting ? s.TtsStage : QuestVoiceStageState.Failed,
                    Tts = unknown ? "后端状态未知；请停止并重启后端" : "本句合成失败，等待下一句",
                    BackendUnknown = s.BackendUnknown || unknown,
                    StageSummary = unknown
                        ? "当前阶段：合成服务状态未知，请停止配音并检查后端"
                        : "当前阶段：本句语音合成失败，等待下一句",
                    Error = value.Detail ?? "配音失败"
                });
                Diagnose(value.Detail ?? "配音失败");
                break;
        }
    }

    /// <summary>单帧错误可恢复，完整异常进入宿主日志。</summary>
    private void OnObservationFailed(Exception exception)
    {
        _overlay.ObservationFailed(exception);
        _logger.LogWarning(exception, "QuestVoice 单帧处理失败，继续等待下一帧");
        Update(s => s with
        {
            OverallStatus = "异常",
            OverallStatusState = QuestVoiceStageState.Failed,
            OcrStage = QuestVoiceStageState.Failed,
            DialogueState = "单帧识别异常",
            StageSummary = "当前阶段：本帧识别异常，继续等待下一帧",
            Ocr = "本帧失败，等待下一帧",
            Error = exception.Message
        });
        Diagnose(exception.Message);
    }

    /// <summary>处理确认离开对白界面时的状态复位。</summary>
    private void HandleLeftDialogue(long epoch, long? revision, long semanticGeneration)
    {
        var wasInDialogue = false;
        lock (_statusGate)
        {
            if (!_versions.TryLeave(epoch, revision, semanticGeneration)) return;
            wasInDialogue = _inDialogue;
            _inDialogue = false;
            _acceptedCandidate = null;
        }
        Update(s => ResetStages(s) with
        {
            OverallStatus = s.IsRunning && string.IsNullOrEmpty(s.Error) && !s.BackendUnknown ? "等待对白" : s.OverallStatus,
            OverallStatusState = s.IsRunning && string.IsNullOrEmpty(s.Error) && !s.BackendUnknown
                ? QuestVoiceStageState.InProgress : s.OverallStatusState,
            DialogueState = s.IsRunning ? "等待对白" : "未运行",
            Ocr = "已离开对白",
            StageSummary = "当前阶段：已离开对话界面，等待下一段对白"
        });
        if (wasInDialogue) RecordEvent("已离开对话界面");
    }

    /// <summary>生成实际声线路由展示文本，明确区分映射命中、固定默认声线与兜底原因。</summary>
    private string FormatRoutedVoice(VoiceRoutingResult? routing) =>
        QuestVoiceStatus.FormatRoutedVoice(routing, _activeVoiceName);

    /// <summary>检查事件是否早于当前已推进的对白周期和视觉版本。</summary>
    private bool IsStale(long epoch, long revision) => _versions.IsStale(epoch, revision);

    /// <summary>推进周期与视觉版本；若进入新版本则清理上一版本的候选标记。</summary>
    private bool AdvanceVersion(long epoch, long revision, long semanticGeneration)
    {
        if (!_versions.TryAdvance(epoch, revision, semanticGeneration, out var newIdentity)) return false;
        if (newIdentity) _acceptedCandidate = null;
        return true;
    }

    /// <summary>重置六个阶段节点为尚未开始状态。</summary>
    private static QuestVoiceStatus ResetStages(QuestVoiceStatus status) => status with
    {
        SceneStage = QuestVoiceStageState.Pending,
        OcrStage = QuestVoiceStageState.Pending,
        SpeakerStage = QuestVoiceStageState.Pending,
        RoutingStage = QuestVoiceStageState.Pending,
        TtsStage = QuestVoiceStageState.Pending,
        PlaybackStage = QuestVoiceStageState.Pending
    };

    /// <summary>不在核心锁里等待 UI；订阅者只收到不可变快照。</summary>
    private void Update(Func<QuestVoiceStatus, QuestVoiceStatus> change)
    {
        QuestVoiceStatus status;
        lock (_statusGate) { status = change(_status); if (status == _status) return; _status = status; }
        Changed?.Invoke(status);
    }

    /// <summary>记录简短界面反馈事件（最多保留 8 条），不重复写入宿主文件日志。</summary>
    private void RecordEvent(string message)
    {
        string lines;
        string[] snapshot;
        lock (_statusGate)
        {
            if (_lastEventText == message) return;
            _lastEventText = message;
            _diagnostics.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
            while (_diagnostics.Count > MaxRecentEvents) _diagnostics.Dequeue();
            snapshot = _diagnostics.ToArray();
            lines = string.Join(Environment.NewLine, snapshot);
        }
        Update(s => s with { Diagnostics = lines, RecentEvents = snapshot });
    }

    /// <summary>保留最多八条简短诊断，并将重要生命周期或故障同步写入宿主日志。</summary>
    private void Diagnose(string message)
    {
        RecordEvent(message);
        _logger.LogInformation("QuestVoice：{Message}", message);
    }

}
