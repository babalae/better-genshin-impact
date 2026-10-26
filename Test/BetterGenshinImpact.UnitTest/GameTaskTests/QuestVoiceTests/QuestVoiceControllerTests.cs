using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>宿主启停、在途帧和重启的所有权测试，不启动主程序或游戏。</summary>
public sealed class QuestVoiceControllerTests
{
    /// <summary>多次启动共用一轮，停止后重新创建完整会话。</summary>
    [Fact]
    public async Task RepeatedStartIsSingleSessionAndRestartCreatesFreshSession()
    {
        var factory = new Factory();
        await using var controller = new QuestVoiceSessionController(factory);
        await Task.WhenAll(controller.StartAsync(), controller.StartAsync());
        Assert.Equal(1, factory.Created);
        Assert.True(controller.IsRunning);
        await Task.WhenAll(controller.StopAsync(), controller.StopAsync());
        Assert.Equal(1, factory.Last!.Disposed);
        Assert.False(controller.IsRunning);
        await controller.StartAsync();
        Assert.Equal(2, factory.Created);
    }

    /// <summary>下一帧不积压，停止必须等当前观察退出才释放依赖。</summary>
    [Fact]
    public async Task StopCancelsAndDrainsOwnedObservationBeforeDispose()
    {
        var factory = new Factory();
        await using var controller = new QuestVoiceSessionController(factory);
        await controller.StartAsync();
        Assert.True(controller.TryObserve(new byte[] { 1 }, TimeSpan.FromMilliseconds(1)));
        await factory.Last!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(controller.TryObserve(new byte[] { 2 }, TimeSpan.FromMilliseconds(2)));
        await controller.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(factory.Last.ObservationCancelled);
        Assert.Equal(1, factory.Last.Disposed);
        Assert.False(controller.TryObserve(new byte[] { 3 }, TimeSpan.FromMilliseconds(3)));
    }

    /// <summary>某帧识别异常不会让整个会话失去后续帧。</summary>
    [Fact]
    public async Task FailedFrameReleasesReservationAndCanRecover()
    {
        var factory = new Factory();
        await using var controller = new QuestVoiceSessionController(factory);
        await controller.StartAsync();
        factory.Last!.FailObservation = true;
        Assert.True(controller.TryObserve(new byte[] { 1 }, TimeSpan.FromMilliseconds(1)));
        await factory.Last.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // 失败被所有者观察并报告，等待预留槽归还。
        for (var i = 0; i < 100 && controller.IsObserving; i++) await Task.Delay(10);
        Assert.False(controller.IsObserving);
        factory.Last.FailObservation = false;
        Assert.True(controller.TryObserve(new byte[] { 2 }, TimeSpan.FromMilliseconds(2)));
        await controller.StopAsync();
    }

    /// <summary>运行页流程状态与声线路由结果展示文案保持真实映射。</summary>
    [Fact]
    public void StatusStageLabelsAndRoutedVoiceDisplayReflectRealRoutingOutcome()
    {
        var initial = new QuestVoiceStatus();
        Assert.Equal("已停止", initial.OverallStatus);
        Assert.Equal(QuestVoiceStageState.Pending, initial.SceneStage);
        Assert.Equal("未开始", initial.SceneStageText);
        Assert.False(initial.HasError);
        Assert.False(initial.HasRecentEvents);

        var active = initial with
        {
            SceneStage = QuestVoiceStageState.Completed,
            OcrStage = QuestVoiceStageState.Completed,
            SpeakerStage = QuestVoiceStageState.Completed,
            RoutingStage = QuestVoiceStageState.Completed,
            TtsStage = QuestVoiceStageState.InProgress,
            PlaybackStage = QuestVoiceStageState.Failed,
            Error = "合成超时",
            RecentEvents = ["21:31:05  检测到对话界面"]
        };
        Assert.Equal("已完成", active.SceneStageText);
        Assert.Equal("进行中", active.TtsStageText);
        Assert.Equal("异常", active.PlaybackStageText);
        Assert.True(active.HasError);
        Assert.True(active.HasRecentEvents);

        var voiceBinding = new VoiceBinding("甘雨", "ganyu.ckpt", "ganyu.pth", [new ReferenceAudio("ref.wav", "原文")]);
        var canonicalHit = new VoiceRoutingResult(
            new SpeakerRoutingInput("甘雨", SpeakerNameStatus.Accepted),
            new SpeakerRoute(RoutingLayer.Canonical, "genshin.ganyu", "voice.ganyu", "ExactName"),
            "sha", "voice.ganyu", "voice.ganyu", voiceBinding, null, null, "甘雨");
        Assert.Equal("甘雨（角色映射命中）", QuestVoiceStatus.FormatRoutedVoice(canonicalHit, "默认 NPC"));

        var fallbackHit = canonicalHit with { FallbackReason = "UnknownName" };
        Assert.Equal("甘雨（兜底：UnknownName）", QuestVoiceStatus.FormatRoutedVoice(fallbackHit, "默认 NPC"));
        Assert.Equal("默认 NPC（默认声线）", QuestVoiceStatus.FormatRoutedVoice(null, "默认 NPC"));
    }

    /// <summary>模拟外部设备边界，仍运行真实会话所有者。</summary>
    private sealed class Factory : IQuestVoiceSessionFactory
    {
        /// <summary>创建次数。</summary>
        public int Created { get; private set; }
        /// <summary>最近创建的会话。</summary>
        public Session? Last { get; private set; }
        /// <summary>创建一轮独立依赖。</summary>
        public Task<IQuestVoiceSession> CreateAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Created++;
            return Task.FromResult<IQuestVoiceSession>(Last = new Session());
        }
    }

    /// <summary>可控观察等待器。</summary>
    private sealed class Session : IQuestVoiceSession
    {
        /// <summary>事件由真实核心测试覆盖，此替身只测试所有权。</summary>
        public event Action<ContinuousFrameEvent>? FrameEvent { add { } remove { } }
        /// <summary>事件由真实核心测试覆盖。</summary>
        public event Action<ContinuousVoiceEvent>? VoiceEvent { add { } remove { } }
        /// <summary>已进入观察。</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>释放次数。</summary>
        public int Disposed { get; private set; }
        /// <summary>是否收到取消。</summary>
        public bool ObservationCancelled { get; private set; }
        /// <summary>制造一次 OCR 错误。</summary>
        public bool FailObservation { get; set; }
        /// <summary>无设备启动。</summary>
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        /// <summary>等待取消以验证停止时仍持有帧。</summary>
        public async Task ObserveAsync(long sequence, TimeSpan at, ReadOnlyMemory<byte> bytes, CancellationToken token, bool dialogueExitConfirmed = false)
        {
            Entered.TrySetResult();
            if (FailObservation) throw new InvalidOperationException("controlled OCR failure");
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { ObservationCancelled = true; throw; }
        }
        /// <summary>停止由 controller 的 token 驱动。</summary>
        public Task StopAsync() => Task.CompletedTask;
        /// <summary>退出时中断连接。</summary>
        public void AbortBackend() { }
        /// <summary>释放验证。</summary>
        public ValueTask DisposeAsync() { Disposed++; return ValueTask.CompletedTask; }
    }
}
