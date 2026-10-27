using System;
using System.Globalization;
using System.Text;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using Serilog.Events;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;

/// <summary>将真实业务事件投影为简短摘要；不参与OCR、候选接受或音频调度。</summary>
public sealed class QuestVoiceOverlayPresenter(Action<LogEventLevel, string> publish, QuestVoiceOverlayOptions? options = null)
{
    /// <summary>不同业务泵可能并发通知，只保护小型展示状态；发布端必须非阻塞。</summary>
    private readonly object _gate = new();
    /// <summary>集中展示选项，不依赖悬浮窗口或页面生命周期。</summary>
    private readonly QuestVoiceOverlayOptions _options = options ?? new();
    /// <summary>独立事件流的最后序号，以及当前视觉版本和周期。</summary>
    private long _frameSequence, _voiceSequence, _revision, _epoch;
    /// <summary>语音代际独立于视觉版本，只有语义更新才清理已接受的工作身份。</summary>
    private long _semanticGeneration;
    /// <summary>单轮展示生命周期，停止意图立即屏蔽迟到事件。</summary>
    private bool _open, _accepting, _started, _inDialogue;
    /// <summary>当前版本已显示阶段；稳定图像的复查不能使阶段倒退。</summary>
    private int _stage;
    /// <summary>跨视觉版本保持当前可见阶段，版本变化本身不重复相同提示。</summary>
    private string? _lastStage;
    /// <summary>只保留业务身份，不额外缓存整段OCR原文。</summary>
    private (long Revision, long Epoch, long Source)? _accepted;
    /// <summary>当前版本最近的简洁错误，重复故障不刷屏。</summary>
    private string? _lastError;

    /// <summary>核心已排空旧会话后准备新一轮；真实Started事件到来才显示已启动。</summary>
    public void BeginSession()
    {
        lock (_gate)
        {
            _frameSequence = _voiceSequence = _revision = _epoch = 0;
            _semanticGeneration = 0;
            _open = _accepting = true;
            _started = _inDialogue = false;
            _stage = 0; _accepted = null; _lastError = _lastStage = null;
        }
    }

    /// <summary>只正式接受的候选显示正文，Observed/复查/丢弃均不冒充识别成功。</summary>
    public void OnFrame(ContinuousFrameEvent value)
    {
        lock (_gate)
        {
            if (!_accepting || value.Sequence <= _frameSequence) return;
            _frameSequence = value.Sequence;
            if (!Advance(value.Revision, value.Epoch, value.SemanticGeneration)) return;
            switch (value.Kind)
            {
                case ContinuousFrameEventKind.Started:
                    if (_started) break;
                    _started = true;
                    publish(LogEventLevel.Information, "自动配音已启动");
                    Stage(1, "等待对白");
                    break;
                case ContinuousFrameEventKind.Observed when value.Inspection?.Selection.Decision.Kind == SceneDecisionKind.Selected:
                    if (_inDialogue) break;
                    _inDialogue = true;
                    Stage(2, "已检测到对白画面");
                    Stage(3, "等待对白稳定");
                    break;
                case ContinuousFrameEventKind.Invalidated:
                    if (_inDialogue) Stage(3, "等待对白稳定");
                    break;
                case ContinuousFrameEventKind.OcrStarted:
                    Stage(4, "正在 OCR 识别");
                    break;
                case ContinuousFrameEventKind.CandidateAccepted when value.Ocr is { } ocr && value.SourceSequence is { } source:
                    var identity = (value.Revision, value.Epoch, source);
                    if (_accepted == identity) break;
                    _accepted = identity;
                    _inDialogue = true;
                    _stage = 5;
                    _lastStage = "OCR 识别成功";
                    if (_options.ShowDialogue)
                    {
                        var name = DisplayText(ocr.Speaker, 64);
                        var text = DisplayText(ocr.Dialogue, 800);
                        publish(LogEventLevel.Information, name.Length == 0 ? $"OCR · {text}" : $"OCR · {name}：{text}");
                    }
                    else if (_options.ShowStages) publish(LogEventLevel.Information, "OCR 识别成功");
                    break;
                case ContinuousFrameEventKind.LeftDialogue:
                    _inDialogue = false;
                    Stage(1, "等待对白");
                    break;
                case ContinuousFrameEventKind.Failed:
                    Error("本次识别失败，请查看文件日志");
                    break;
            }
        }
    }

    /// <summary>以Work里的原候选版本判断迟到事件，不能用事件投递时的新版本冒充旧工作的版本。</summary>
    public void OnVoice(ContinuousVoiceEvent value)
    {
        lock (_gate)
        {
            if (!_open || value.Sequence <= _voiceSequence) return;
            _voiceSequence = value.Sequence;
            if (!_accepting)
            {
                // 停止中的真实停止故障仍须通知，但旧合成/播放阶段不能恢复运行显示。
                if (value.Kind == ContinuousVoiceEventKind.PlaybackStopFailed) Error("音频播放失败，请检查输出设备");
                return;
            }
            var candidate = value.Work?.Candidate;
            if (value.Kind == ContinuousVoiceEventKind.LeftDialogue)
            {
                // 无Work的Revision属于语音代际，不能拿它推进视觉版本。
                if (!Advance(_revision, value.Epoch, value.Revision)) return;
                _inDialogue = false;
                Stage(1, "等待对白");
                return;
            }
            if (candidate is null || candidate.Epoch != _epoch
                || _accepted != (candidate.Revision, candidate.Epoch, candidate.SourceSequence)
                || candidate.SemanticGeneration is { } generation && generation != _semanticGeneration) return;
            switch (value.Kind)
            {
                case ContinuousVoiceEventKind.SynthesisStarted: Stage(6, "正在合成语音"); break;
                case ContinuousVoiceEventKind.PlaybackStarted: Stage(7, "正在播放语音"); break;
                case ContinuousVoiceEventKind.PlaybackCompleted when value.Error is not null:
                case ContinuousVoiceEventKind.PlaybackStopFailed:
                    Error("音频播放失败，请检查输出设备");
                    break;
                case ContinuousVoiceEventKind.CandidateFailed when value.Error != VoiceErrorCode.Cancelled:
                    Error(value.Error switch
                    {
                        VoiceErrorCode.Unreachable => "语音合成服务连接失败，请检查后端",
                        VoiceErrorCode.Timeout or VoiceErrorCode.InterruptedResponse or VoiceErrorCode.BackendUnknown
                            => "合成服务状态未知，请停止配音并检查后端",
                        VoiceErrorCode.PlaybackDevice => "音频播放失败，请检查输出设备",
                        VoiceErrorCode.SaveFailed => "音频保存失败，请检查磁盘与文件日志",
                        _ => "语音合成失败，请检查后端与文件日志"
                    });
                    break;
            }
        }
    }

    /// <summary>宿主异常只提取责任类别，不向悬浮框复制路径、请求参数或异常堆栈。</summary>
    public void ObservationFailed(Exception exception)
    {
        lock (_gate)
        {
            if (!_accepting) return;
            Error(exception is OcrException { Code: OcrErrorCode.ModelMissing or OcrErrorCode.ModelInitialization }
                ? "OCR 模型加载失败，请检查模型配置" : "本次识别失败，请查看文件日志");
        }
    }

    /// <summary>启动失败不制造运行状态，详细异常由原文件日志保存。</summary>
    public void StartupFailed()
    {
        lock (_gate)
        {
            if (!_open) return;
            Error("自动配音启动失败，请检查配置与文件日志");
            _accepting = _open = false;
        }
    }

    /// <summary>实际运行环境断开才显示，不猜测游戏状态。</summary>
    public void Disconnected()
    {
        lock (_gate) { if (_open) Error("游戏连接已断开"); }
    }

    /// <summary>用户或宿主请求停止时，立即拒绝尚未到达的旧工作消息。</summary>
    public void StopRequested()
    {
        lock (_gate) _accepting = false;
    }

    /// <summary>真实资源排空后仅显示一次停止，未启动过的宿主释放不刷通知。</summary>
    public void Stopped()
    {
        lock (_gate)
        {
            if (!_open) return;
            _accepting = _open = false;
            publish(LogEventLevel.Information, "自动配音已停止");
        }
    }

    /// <summary>排空故障保留必要通知，不能恢复已拒绝的旧状态消息。</summary>
    public void StopFailed()
    {
        lock (_gate) { if (_open) Error("配音停止失败，请查看文件日志"); }
    }

    /// <summary>周期和版本单调推进；每个新身份允许相同文本重新显示。</summary>
    private bool Advance(long revision, long epoch, long semanticGeneration = 0)
    {
        if (epoch < _epoch || epoch == _epoch && revision < _revision) return false;
        if (epoch != _epoch || semanticGeneration > _semanticGeneration)
        {
            _semanticGeneration = semanticGeneration;
            _accepted = null; _stage = 0; _lastError = null;
        }
        _epoch = epoch; _revision = revision;
        return true;
    }

    /// <summary>同版本阶段只前进，周期OCR复查不把播放中状态退回识别中。</summary>
    private void Stage(int stage, string text)
    {
        if (stage <= _stage) return;
        _stage = stage;
        if (_lastStage == text) return;
        _lastStage = text;
        if (_options.ShowStages) publish(LogEventLevel.Information, text);
    }

    /// <summary>错误不受开发阶段开关影响，同版本同故障只显示一次。</summary>
    private void Error(string text)
    {
        if (_lastError == text) return;
        _lastError = text;
        publish(LogEventLevel.Warning, text);
    }

    /// <summary>仅在需要显示新正文时按Rune限长，清理控制/格式字符并合并换行；原文不变。</summary>
    private static string DisplayText(string? text, int maximum)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var result = new StringBuilder();
        var count = 0;
        var space = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count++ >= maximum) { result.Append('…'); break; }
            // XML 1.0不能表示这两个BMP非字符；避免一条OCR文本使整批富文本解析失败。
            if (rune.Value is 0xFFFE or 0xFFFF) { result.Append('\uFFFD'); space = false; continue; }
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsWhiteSpace(rune) || category is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                if (result.Length > 0 && !space) result.Append(' ');
                space = true;
            }
            else { result.Append(rune); space = false; }
        }
        return result.ToString().Trim();
    }
}
