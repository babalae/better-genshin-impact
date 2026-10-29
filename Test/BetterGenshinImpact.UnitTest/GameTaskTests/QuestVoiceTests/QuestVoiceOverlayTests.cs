using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>悬浮摘要身份、顺序和结构化日志分流回归。</summary>
public sealed class QuestVoiceOverlayTests
{
    /// <summary>详细证据保留文件，摘要与其他模块通知进入悬浮，独立于全局日志等级。</summary>
    [Fact]
    public void Structured_routing_keeps_diagnostics_in_file_and_other_modules_visible()
    {
        var path = Path.Combine(Path.GetTempPath(), "qv-log-" + Guid.NewGuid().ToString("N") + ".log");
        var overlay = new Events();
        try
        {
            using (var logger = new LoggerConfiguration().MinimumLevel.Debug()
                .WriteTo.Logger(file => file.Filter.ByExcluding(QuestVoiceLog.IsSummary).WriteTo.File(path))
                .WriteTo.Logger(ui => ui.Filter.ByIncludingOnly(QuestVoiceLog.IsOverlayVisible).WriteTo.Sink(overlay))
                .CreateLogger())
            {
                QuestVoiceLog.ForDiagnostic(logger).Debug("SceneSelected {@Evidence}", new { Roi = 123, Sha256 = "proof" });
                QuestVoiceLog.ForDiagnostic(logger).Information("Voice {@Work}", new { Revision = 9, Epoch = 2 });
                logger.ForContext(QuestVoiceLog.KindProperty, QuestVoiceLog.SummaryKind).Information("OCR · 派蒙：你好");
                logger.ForContext("SourceContext", "BetterGenshinImpact.Service.QuestVoiceService")
                    .Warning(new InvalidOperationException("private detail"), "详细异常");
                logger.ForContext("SourceContext", "BetterGenshinImpact.GameTask.AutoPick.AutoPickTrigger").Information("拾取完成");
            }
            var file = File.ReadAllText(path);
            Assert.Contains("proof", file);
            Assert.Contains("Revision", file);
            Assert.Contains("private detail", file);
            Assert.DoesNotContain("OCR ·", file);
            Assert.Equal(2, overlay.Items.Count);
            Assert.Contains(overlay.Items, item => item.RenderMessage().Contains("OCR · 派蒙：你好"));
            Assert.Contains(overlay.Items, item => item.RenderMessage().Contains("拾取完成"));
        }
        finally { File.Delete(path); }
    }

    /// <summary>正式候选才显示正文，周期性复查和丢弃结果不能成为成功识别。</summary>
    [Fact]
    public void Only_accepted_candidates_show_text_and_rechecks_do_not_regress_stage()
    {
        var (presenter, output) = Create();
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.OcrStarted));
        presenter.OnFrame(Frame(3, ContinuousFrameEventKind.OcrCompleted, text: "未接受"));
        Assert.DoesNotContain(output, item => item.Text.Contains("未接受"));
        presenter.OnFrame(Frame(4, ContinuousFrameEventKind.CandidateAccepted, text: "你好"));
        var count = output.Count;
        presenter.OnFrame(Frame(5, ContinuousFrameEventKind.OcrStarted));
        presenter.OnFrame(Frame(6, ContinuousFrameEventKind.OcrCompleted, text: "你好"));
        presenter.OnFrame(Frame(7, ContinuousFrameEventKind.Discarded, text: "无效旧结果"));
        Assert.Equal(count, output.Count);
        Assert.Single(output.Where(item => item.Text.Contains("OCR · 派蒙：你好")));
    }

    /// <summary>画面版本更新不会隐藏仍有效的旧来源合成及播放事件。</summary>
    [Fact]
    public void Visual_recheck_keeps_accepted_voice_visible()
    {
        var (presenter, output) = Create();
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.CandidateAccepted, text: "你好"));
        presenter.OnVoice(Voice(1, ContinuousVoiceEventKind.SynthesisStarted));
        presenter.OnFrame(Frame(3, ContinuousFrameEventKind.Invalidated, revision: 2));
        presenter.OnVoice(Voice(2, ContinuousVoiceEventKind.PlaybackStarted));
        Assert.Equal("正在播放语音", output.Last().Text);
    }

    /// <summary>真正语义失效后，旧来源即使仍在同一周期也不能恢复为当前阶段。</summary>
    [Fact]
    public void Semantic_invalidation_rejects_late_voice_without_mixing_visual_versions()
    {
        var (presenter, output) = Create();
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.CandidateAccepted, text: "旧句") with { SemanticGeneration = 1 });
        presenter.OnVoice(Voice(1, ContinuousVoiceEventKind.SynthesisStarted, generation: 1));
        presenter.OnFrame(Frame(3, ContinuousFrameEventKind.Invalidated, revision: 20) with { SemanticGeneration = 2 });
        var count = output.Count;
        presenter.OnVoice(Voice(2, ContinuousVoiceEventKind.PlaybackStarted, generation: 1));
        Assert.Equal(count, output.Count);
        presenter.OnFrame(Frame(4, ContinuousFrameEventKind.CandidateAccepted, revision: 20, source: 30, text: "新句")
            with { SemanticGeneration = 3 });
        presenter.OnVoice(Voice(3, ContinuousVoiceEventKind.PlaybackStarted, revision: 20, source: 30, generation: 3));
        Assert.Equal("正在播放语音", output.Last().Text);
    }

    /// <summary>相同文字在新对白身份或周期内仍应显示，不能永久按文本去重。</summary>
    [Fact]
    public void Identical_text_in_a_new_dialogue_identity_is_shown_again()
    {
        var (presenter, output) = Create();
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.CandidateAccepted, text: "你好"));
        presenter.OnFrame(Frame(3, ContinuousFrameEventKind.Invalidated, revision: 2));
        presenter.OnFrame(Frame(4, ContinuousFrameEventKind.CandidateAccepted, revision: 2, text: "你好"));
        presenter.OnFrame(Frame(5, ContinuousFrameEventKind.LeftDialogue, revision: 3, epoch: 1));
        presenter.OnFrame(Frame(6, ContinuousFrameEventKind.CandidateAccepted, revision: 4, epoch: 1, text: "你好"));
        Assert.Equal(3, output.Count(item => item.Text.Contains("OCR ·")));
    }

    /// <summary>纯视觉重检保留有效语音；新候选接受后才拒绝旧语音来源。</summary>
    [Fact]
    public void Late_voice_and_frame_events_cannot_roll_back_new_dialogue()
    {
        var (presenter, output) = Create();
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.CandidateAccepted, text: "旧句"));
        presenter.OnVoice(Voice(1, ContinuousVoiceEventKind.SynthesisStarted));
        presenter.OnFrame(Frame(3, ContinuousFrameEventKind.Invalidated, revision: 2));
        presenter.OnFrame(Frame(4, ContinuousFrameEventKind.OcrStarted, revision: 2));
        var count = output.Count;
        presenter.OnVoice(Voice(2, ContinuousVoiceEventKind.SynthesisStarted));
        presenter.OnVoice(Voice(3, ContinuousVoiceEventKind.PlaybackStarted));
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.CandidateAccepted, text: "旧句"));
        Assert.Equal("正在播放语音", output.Last().Text);
        presenter.OnFrame(Frame(5, ContinuousFrameEventKind.CandidateAccepted, revision: 2, source: 20, text: "新句"));
        presenter.OnVoice(Voice(4, ContinuousVoiceEventKind.SynthesisStarted, revision: 2, source: 20));
        presenter.OnVoice(Voice(5, ContinuousVoiceEventKind.PlaybackStarted, revision: 2, source: 20));
        Assert.Equal("正在播放语音", output.Last().Text);
        count = output.Count;
        presenter.OnVoice(Voice(6, ContinuousVoiceEventKind.PlaybackStarted));
        Assert.Equal(count, output.Count);
    }

    /// <summary>停止立即屏蔽旧事件，重新启动允许相同身份的真实新会话。</summary>
    [Fact]
    public void Stop_and_restart_do_not_leak_old_events_or_permanently_deduplicate()
    {
        var (presenter, output) = Create();
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.CandidateAccepted, text: "你好"));
        presenter.StopRequested();
        var count = output.Count;
        presenter.OnVoice(Voice(2, ContinuousVoiceEventKind.PlaybackStarted));
        presenter.OnFrame(Frame(3, ContinuousFrameEventKind.CandidateAccepted, text: "迟到"));
        Assert.Equal(count, output.Count);
        presenter.Stopped(); presenter.Stopped();
        Assert.Single(output.Where(item => item.Text == "自动配音已停止"));
        presenter.BeginSession();
        presenter.OnFrame(Frame(1, ContinuousFrameEventKind.Started, revision: 0));
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.CandidateAccepted, text: "你好"));
        Assert.Equal(2, output.Count(item => item.Text.Contains("OCR ·")));
    }

    /// <summary>阶段只变化一次，稳定画面的重复Observed不持续刷屏。</summary>
    [Fact]
    public void Repeated_observations_and_voice_stages_are_coalesced()
    {
        var (presenter, output) = Create();
        for (var i = 2; i < 100; i++) presenter.OnFrame(Frame(i, ContinuousFrameEventKind.Observed, inspection: Selected()));
        Assert.Single(output.Where(item => item.Text == "已检测到对白画面"));
        Assert.Single(output.Where(item => item.Text == "等待对白稳定"));
        presenter.OnFrame(Frame(100, ContinuousFrameEventKind.CandidateAccepted, text: "你好"));
        for (var i = 1; i < 100; i++) presenter.OnVoice(Voice(i, ContinuousVoiceEventKind.SynthesisStarted));
        Assert.Single(output.Where(item => item.Text == "正在合成语音"));
    }

    /// <summary>首帧Selected紧接视觉版本失效时，实际阶段仍是等待稳定，不重复通知。</summary>
    [Fact]
    public void Version_changes_do_not_repeat_an_unchanged_visible_stage()
    {
        var (presenter, output) = Create();
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.Observed, revision: 0, inspection: Selected()));
        presenter.OnFrame(Frame(3, ContinuousFrameEventKind.Invalidated, revision: 1));
        presenter.OnFrame(Frame(4, ContinuousFrameEventKind.Invalidated, revision: 2));
        Assert.Single(output.Where(item => item.Text == "等待对白稳定"));
    }

    /// <summary>简洁错误不包含原始路径、Token或异常堆栈，重复故障有界。</summary>
    [Theory]
    [InlineData(VoiceErrorCode.Unreachable, "语音合成服务连接失败，请检查后端")]
    [InlineData(VoiceErrorCode.PlaybackDevice, "音频播放失败，请检查输出设备")]
    public void Errors_are_actionable_and_do_not_render_raw_details(VoiceErrorCode error, string expected)
    {
        var (presenter, output) = Create();
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.CandidateAccepted, text: "你好"));
        presenter.OnVoice(Voice(1, ContinuousVoiceEventKind.CandidateFailed, error: error));
        presenter.OnVoice(Voice(2, ContinuousVoiceEventKind.CandidateFailed, error: error));
        Assert.Single(output.Where(item => item.Text == expected));
        Assert.DoesNotContain(output, item => item.Text.Contains("secret"));
        Assert.Equal(LogEventLevel.Warning, output.Last().Level);
    }

    /// <summary>阶段和正文集中开关独立，生命周期与错误不被关闭。</summary>
    [Fact]
    public void Release_verbosity_options_keep_lifecycle_and_errors()
    {
        var output = new List<(LogEventLevel Level, string Text)>();
        var presenter = new QuestVoiceOverlayPresenter((level, text) => output.Add((level, text)),
            new QuestVoiceOverlayOptions(ShowStages: false, ShowDialogue: false));
        presenter.BeginSession(); presenter.OnFrame(Frame(1, ContinuousFrameEventKind.Started));
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.OcrStarted));
        presenter.OnFrame(Frame(3, ContinuousFrameEventKind.CandidateAccepted, text: "私密正文"));
        presenter.ObservationFailed(new OcrException(OcrErrorCode.ModelMissing, "secret path"));
        presenter.StopRequested(); presenter.Stopped();
        Assert.Equal(new[] { "自动配音已启动", "OCR 模型加载失败，请检查模型配置", "自动配音已停止" }, output.Select(item => item.Text));
    }

    /// <summary>停止期间的实际播放停止故障不能被普通迟到状态过滤一起吞掉。</summary>
    [Fact]
    public void Playback_stop_failure_remains_visible_while_stopping()
    {
        var (presenter, output) = Create();
        presenter.OnFrame(Frame(2, ContinuousFrameEventKind.CandidateAccepted, text: "你好"));
        presenter.StopRequested();
        presenter.OnVoice(Voice(1, ContinuousVoiceEventKind.PlaybackStopFailed, error: VoiceErrorCode.PlaybackDevice));
        Assert.Contains(output, item => item.Text == "音频播放失败，请检查输出设备");
        presenter.Stopped();
        var count = output.Count;
        presenter.OnVoice(Voice(2, ContinuousVoiceEventKind.PlaybackStopFailed, error: VoiceErrorCode.PlaybackDevice));
        Assert.Equal(count, output.Count);
    }

    /// <summary>换行、XML特殊字符和Unicode长文本安全限长，不破坏原始业务OCR结果。</summary>
    [Fact]
    public void Dialogue_display_is_sanitized_and_bounded_without_mutating_source()
    {
        var (presenter, output) = Create();
        var raw = "<旅行者>&\"\r\n\0\ufffe\uffff" + string.Concat(Enumerable.Repeat("😀", 1000));
        var value = Frame(2, ContinuousFrameEventKind.CandidateAccepted, text: raw);
        presenter.OnFrame(value);
        var display = output.Single(item => item.Text.StartsWith("OCR ·")).Text;
        Assert.Contains("<旅行者>&\"", display);
        Assert.DoesNotContain('\0', display);
        Assert.DoesNotContain('\n', display);
        Assert.DoesNotContain('\ufffe', display);
        Assert.DoesNotContain('\uffff', display);
        Assert.EndsWith("…", display);
        Assert.True(display.EnumerateRunes().Count() < 900);
        Assert.Equal(raw, value.Ocr!.Dialogue);
    }

    /// <summary>真实OCR泵错误的异常对象和堆栈仍进入诊断通道，不止一段展示字符串。</summary>
    [Fact]
    public async Task Ocr_failure_retains_original_exception_in_diagnostic_log()
    {
        var captured = new Events();
        using var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(captured).CreateLogger();
        await using var fixture = new ContinuousFrameFixture();
        await using var session = new ContinuousFrameSession(ContinuousFrameGateTests.Settings, fixture.Inspect, fixture.Ocr,
            logger: QuestVoiceLog.ForDiagnostic(logger));
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Event += value => { if (value.Kind == ContinuousFrameEventKind.Failed) failed.TrySetResult(); };
        await session.StartAsync();
        for (var i = 1; i <= 3; i++) await session.ObserveAsync(i, TimeSpan.FromMilliseconds(i * 200), new byte[] { 1 });
        var work = await fixture.Ocr.NextAsync();
        var error = new OcrException(OcrErrorCode.Inference, "controlled inference failure");
        work.Result.TrySetException(error);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(captured.Items, item => ReferenceEquals(item.Exception, error) && !QuestVoiceLog.IsOverlayVisible(item));
        Assert.False(string.IsNullOrEmpty(error.StackTrace));
    }

    /// <summary>真实合成泵失败保留异常证据，简洁摘要不替代后台诊断。</summary>
    [Fact]
    public async Task Synthesis_failure_retains_original_exception_in_diagnostic_log()
    {
        var captured = new Events();
        using var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(captured).CreateLogger();
        await using var fixture = new ContinuousVoiceFixture();
        await fixture.Catalog.WriteAsync();
        await using var session = new ContinuousVoiceSession(fixture.Settings, fixture.Engine, new WavProcessor(),
            fixture.Artifacts, fixture.Player, fixture.Routing, QuestVoiceLog.ForDiagnostic(logger));
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Event += value => { if (value.Kind == ContinuousVoiceEventKind.CandidateFailed) failed.TrySetResult(); };
        await session.StartAsync();
        session.Invalidate(1); session.Offer(ContinuousVoiceFixture.Candidate(1, "test"));
        var work = await fixture.Engine.NextAsync();
        var error = new VoiceException(VoiceErrorCode.Unreachable, "controlled transport failure");
        work.Result.TrySetException(error);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(captured.Items, item => ReferenceEquals(item.Exception, error) && !QuestVoiceLog.IsOverlayVisible(item));
        Assert.False(string.IsNullOrEmpty(error.StackTrace));
    }

    /// <summary>无姓名时仅显示实际正文；旧周期即使序号较大也不能泄漏。</summary>
    [Fact]
    public void Unknown_speaker_and_stale_epoch_are_handled_without_inventing_identity()
    {
        var (presenter, output) = Create();
        var value = Frame(2, ContinuousFrameEventKind.CandidateAccepted, revision: 4, epoch: 1, text: "实际正文");
        presenter.OnFrame(value with { Ocr = value.Ocr! with { Speaker = null } });
        Assert.Equal("OCR · 实际正文", output.Last().Text);
        var count = output.Count;
        presenter.OnFrame(Frame(100, ContinuousFrameEventKind.CandidateAccepted, revision: 100, epoch: 0, text: "旧周期"));
        presenter.OnVoice(Voice(100, ContinuousVoiceEventKind.SynthesisStarted, revision: 100, epoch: 0));
        Assert.Equal(count, output.Count);
    }

    /// <summary>创建已真实开始的单轮适配器。</summary>
    private static (QuestVoiceOverlayPresenter, List<(LogEventLevel Level, string Text)>) Create()
    {
        var output = new List<(LogEventLevel Level, string Text)>();
        var presenter = new QuestVoiceOverlayPresenter((level, text) => output.Add((level, text)));
        presenter.BeginSession(); presenter.OnFrame(Frame(1, ContinuousFrameEventKind.Started, revision: 0));
        return (presenter, output);
    }

    /// <summary>构造真实契约事件而非解析日志字符串。</summary>
    private static ContinuousFrameEvent Frame(long sequence, ContinuousFrameEventKind kind, long revision = 1,
        long epoch = 0, long source = 10, string? text = null, ContinuousFrameInspection? inspection = null)
        => new(sequence, TimeSpan.Zero, kind, revision, epoch, source, TimeSpan.Zero, "hash",
            Inspection: inspection, Ocr: text is null ? null : new ImageOcrResult(1, 1, new(1, 1, []), [], "派蒙", text,
                new("model", "hash", "DML", 0, null), DateTimeOffset.Now, DateTimeOffset.Now));

    /// <summary>配音事件携带正式候选身份和只应进入文件的详细故障。</summary>
    private static ContinuousVoiceEvent Voice(long sequence, ContinuousVoiceEventKind kind, long revision = 1,
        long epoch = 0, long source = 10, VoiceErrorCode? error = null, long? generation = null)
        => new(sequence, TimeSpan.Zero, kind, generation ?? revision, epoch, source,
            new(new(revision, epoch, source, TimeSpan.Zero, "hash", "layout", "派蒙", SpeakerNameStatus.Accepted, "正文", generation)),
            "secret token or stack", error);

    /// <summary>只有实际Selected事件才表示检测到了对白画面。</summary>
    private static ContinuousFrameInspection Selected() => new(new("hash", 1, 1),
        new(new(SceneDecisionKind.Selected, "layout", "reason", "version", "hash", new Dictionary<string, double>()), null), null);

    /// <summary>测试用接收端，仅收集实际Serilog事件，不替代WPF行为测试。</summary>
    private sealed class Events : ILogEventSink
    {
        /// <summary>保存实际收到的事件供断言。</summary>
        public List<LogEvent> Items { get; } = [];
        /// <summary>正常Serilog投递边界。</summary>
        public void Emit(LogEvent logEvent) => Items.Add(logEvent);
    }
}
