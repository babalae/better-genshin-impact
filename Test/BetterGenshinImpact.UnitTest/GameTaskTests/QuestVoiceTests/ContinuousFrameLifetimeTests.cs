using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousFrameLifetimeTests。</summary>
public sealed class ContinuousFrameLifetimeTests
{
    /// <summary>移植回归验证：CompleteDuringStartWaitsForStartAndDoesNotReopenInput。</summary>
    [Fact]
    public async Task CompleteDuringStartWaitsForStartAndDoesNotReopenInput()
    {
        await using var fixture = new ContinuousFrameFixture();
        await fixture.Voice.StartAsync();
        var voice = new HeldStart(fixture.Voice.Session);
        await using var session = new ContinuousFrameSession(ContinuousFrameGateTests.Settings, fixture.Inspect, fixture.Ocr, voice);
        var start = session.StartAsync();
        var completion = session.CompleteAsync();
        Assert.False(completion.IsCompleted);
        voice.Release.TrySetResult();
        await start; await completion.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ObserveAsync(0, TimeSpan.Zero, new byte[] { 1 }));
        Assert.True(session.Snapshot.Completed);
    }

    /// <summary>移植回归验证：DisposeDuringStartWaitsForTheActualStartExit。</summary>
    [Fact]
    public async Task DisposeDuringStartWaitsForTheActualStartExit()
    {
        await using var fixture = new ContinuousFrameFixture();
        await fixture.Voice.StartAsync();
        var voice = new HeldStart(fixture.Voice.Session);
        var session = new ContinuousFrameSession(ContinuousFrameGateTests.Settings, fixture.Inspect, fixture.Ocr, voice);
        var start = session.StartAsync();
        var dispose = session.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        voice.Release.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => start);
        await dispose.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(session.Snapshot.Stopped);
    }

    /// <summary>移植回归验证：EmptyFirstRecognitionCanAdvanceWhenRecheckConfirmsNewText。</summary>
    [Fact]
    public async Task EmptyFirstRecognitionCanAdvanceWhenRecheckConfirmsNewText()
    {
        await using var fixture = new ContinuousFrameFixture(); await fixture.StartAsync(true);
        await fixture.Stable(0, 0); (await fixture.Ocr.NextAsync()).Complete(" ");
        await fixture.EventAsync(ContinuousFrameEventKind.Discarded, 2);
        var generation = fixture.Session.Snapshot.SemanticGeneration;
        await fixture.Frame(3, 1400); await fixture.Frame(4, 2400);
        (await fixture.Ocr.NextAsync()).Complete("new text");
        await fixture.EventAsync(ContinuousFrameEventKind.CandidateAccepted, 4);
        await fixture.Session.CompleteAsync();
        Assert.True(fixture.Session.Snapshot.SemanticGeneration > generation);
        Assert.Single(fixture.Events, value => value.Kind == ContinuousFrameEventKind.CandidateAccepted);
    }

    /// <summary>移植回归验证：HeldStart。</summary>
    private sealed class HeldStart(IContinuousVoiceSession inner) : IContinuousVoiceSession
    {
        /// <summary>移植回归验证：Release。</summary>
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>启动当前单轮管线；重复调用沿用既有启动任务。</summary>
        public async Task StartAsync(CancellationToken token = default) { await Release.Task; }
        /// <summary>读取当前不可变状态快照。</summary>
        public ContinuousVoiceSnapshot Snapshot => inner.Snapshot;
        /// <summary>按发生顺序发布的业务事件。</summary>
        public event Action<ContinuousVoiceEvent>? Event { add => inner.Event += value; remove => inner.Event -= value; }
        /// <summary>递增视觉版本并丢弃尚未播放的旧内容。</summary>
        public void Invalidate(long revision) => inner.Invalidate(revision);
        /// <summary>只接受当前版本与周期中尚未提交的对白候选。</summary>
        public void Offer(ContinuousDialogueCandidate candidate) => inner.Offer(candidate);
        /// <summary>转发真实路由预检，测试包装器不能静默跳过同句所有权判断。</summary>
        public Task<bool> MatchesCurrentDialogueAsync(ContinuousDialogueCandidate candidate, CancellationToken token = default)
            => inner.MatchesCurrentDialogueAsync(candidate, token);
        /// <summary>离开对白时切换周期，停止播放并等待其实际退出。</summary>
        public Task LeaveDialogueAsync(long revision) => inner.LeaveDialogueAsync(revision);
        /// <summary>取得观察租约，在画面版本判定之前阻止新播放。</summary>
        public IDisposable BeginObservation() => inner.BeginObservation();
        /// <summary>关闭输入后自然排空最新有效候选与播放。</summary>
        public Task CompleteAsync(CancellationToken token = default) => inner.CompleteAsync(token);
        /// <summary>拒绝后续工作、取消未播内容，并等待已拥有的任务排空。</summary>
        public Task StopAsync(CancellationToken token = default) => inner.StopAsync(token);
        /// <summary>异步排空拥有的任务，再释放本轮资源。</summary>
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
