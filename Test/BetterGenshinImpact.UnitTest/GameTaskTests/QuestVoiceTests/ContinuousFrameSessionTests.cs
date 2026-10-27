using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousFrameSessionTests。</summary>
public sealed class ContinuousFrameSessionTests
{
    /// <summary>同句跨视觉版本时合成所有权保持有效。</summary>
    [Fact] public async Task VisualChangePreservesSameDialogueSynthesis()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("晚安啦，小可爱");
        var synthesis = await f.Voice.Engine.NextAsync();
        await f.Stable(3, 500, 2);
        Assert.False(synthesis.Token.IsCancellationRequested);
        (await f.Ocr.NextAsync()).Complete("晚安啦，小可爱");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 5);
        Assert.Single(f.Voice.Engine.All);
    }

    /// <summary>短暂失选只让OCR过期，不废弃有效语音。</summary>
    [Fact] public async Task TransientMissingSelectionPreservesSynthesis()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete();
        var synthesis = await f.Voice.Engine.NextAsync();
        await f.Frame(3, 500, 0);
        Assert.False(synthesis.Token.IsCancellationRequested);
        Assert.Equal(0, f.Session.Snapshot.Epoch);
    }

    /// <summary>单次空识别不构成新语义或对白清空边界。</summary>
    [Fact] public async Task SingleEmptyOcrPreservesSynthesis()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete();
        var synthesis = await f.Voice.Engine.NextAsync();
        await f.Frame(3, 1400); await f.Frame(4, 2400);
        (await f.Ocr.NextAsync()).Complete(" ");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 4);
        Assert.False(synthesis.Token.IsCancellationRequested);
        Assert.Single(f.Voice.Engine.All);
    }
    /// <summary>长句单字变化先取证；下一真实稳定帧可安排确认而无需等周期2秒。</summary>
    [Fact] public async Task LongSingleCharacterChangeNeedsFreshOcrEvidence()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("我们今天一起去远方看风景");
        var first = await f.Voice.Engine.NextAsync();
        await f.Frame(3, 1400); await f.Frame(4, 2400);
        (await f.Ocr.NextAsync()).Complete("我们明天一起去远方看风景");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 4);
        Assert.False(first.Token.IsCancellationRequested);
        await f.Frame(5, 2600); var confirmation = await f.Ocr.NextAsync();
        Assert.Equal(5, confirmation.Frame.Sequence);
        confirmation.Complete("我们明天一起去远方看风景");
        await f.EventAsync(ContinuousFrameEventKind.CandidateAccepted, 5);
        Assert.True(first.Token.IsCancellationRequested);
        Assert.Equal(2, f.Session.Snapshot.SemanticGeneration);
        Assert.Equal(1, f.Session.Snapshot.Revision);
    }

    /// <summary>逗号交替复用首次语音所有权。</summary>
    [Fact] public async Task CommaJitterReusesInFlightSynthesis()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("晚安啦，小可爱");
        var first = await f.Voice.Engine.NextAsync();
        await f.Frame(3, 1400); await f.Frame(4, 2400);
        (await f.Ocr.NextAsync()).Complete("晚安啦小可爱");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 4);
        Assert.False(first.Token.IsCancellationRequested);
        Assert.Equal(1, f.Session.Snapshot.SemanticGeneration);
        Assert.Single(f.Voice.Engine.All);
    }

    /// <summary>明确清空废弃旧未播语音；同文恢复属于新话轮。</summary>
    [Fact] public async Task ReliableDialogueClearAllowsSameTextInNewOccurrence()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("重复正文");
        var first = await f.Voice.Engine.NextAsync();
        f.Inspect.ClearDialogueEvidence = true;
        await f.Frame(3, 500); Assert.False(first.Token.IsCancellationRequested);
        await f.Frame(4, 700); Assert.True(first.Token.IsCancellationRequested);
        Assert.Contains(f.Events, e => e.Detail == "ConfirmedDialogueClear");
        first.Complete(); f.Inspect.ClearDialogueEvidence = false;
        await f.Stable(5, 800); (await f.Ocr.NextAsync()).Complete("重复正文");
        var second = await f.Voice.Engine.NextAsync();
        Assert.Equal("重复正文", second.Request.Text);
        Assert.Equal(1, f.Voice.Session.Snapshot.Synthesizing!.Candidate.DialogueOccurrence);
    }

    /// <summary>确认期间视觉版本变化后，过期OCR不能接受候选或取消旧语音。</summary>
    [Fact] public async Task VisualChangeDiscardsPendingTextConfirmationEvidence()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("我们今天一起去远方看风景");
        var first = await f.Voice.Engine.NextAsync();
        await f.Frame(3, 1400); await f.Frame(4, 2400);
        (await f.Ocr.NextAsync()).Complete("我们明天一起去远方看风景");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 4);
        await f.Frame(5, 2600); var stale = await f.Ocr.NextAsync();
        await f.Frame(6, 2700, 2); stale.Complete("我们明天一起去远方看风景");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 5);
        Assert.False(first.Token.IsCancellationRequested);
        Assert.Equal(1, f.Session.Snapshot.SemanticGeneration);
    }
    /// <summary>变体交替最多引发两次额外OCR，不按时间自动接受。</summary>
    [Fact] public async Task TargetedConfirmationBudgetIsBoundedAndNeverAcceptsOnTimeAlone()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("我们今天一起去远方看风景");
        var first = await f.Voice.Engine.NextAsync();
        await f.Frame(3, 1400); await f.Frame(4, 2400);
        (await f.Ocr.NextAsync()).Complete("我们明天一起去远方看风景");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 4);
        await f.Frame(5, 2600); (await f.Ocr.NextAsync()).Complete("我们今天一同去远方看风景");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 5);
        await f.Frame(6, 2800); (await f.Ocr.NextAsync()).Complete("我们明天一起去远方看风景");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 6);
        await f.Frame(7, 3000); await f.Frame(8, 3200);
        Assert.Equal(4, f.Ocr.All.Count);
        Assert.False(first.Token.IsCancellationRequested);
        Assert.Equal(1, f.Session.Snapshot.SemanticGeneration);
    }

    /// <summary>可靠清空保留已经播放的旧句，新话轮同文Ready后才接替。</summary>
    [Fact] public async Task ReliableDialogueClearPreservesPlayingAudioUntilNewOccurrenceReady()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("重复正文");
        (await f.Voice.Engine.NextAsync()).Complete(); var playing = await f.Voice.Player.NextAsync();
        f.Inspect.ClearDialogueEvidence = true;
        await f.Frame(3, 500); await f.Frame(4, 700);
        Assert.False(playing.Stop.Task.IsCompleted);
        f.Inspect.ClearDialogueEvidence = false;
        await f.Stable(5, 800); (await f.Ocr.NextAsync()).Complete("重复正文");
        var second = await f.Voice.Engine.NextAsync();
        Assert.False(playing.Stop.Task.IsCompleted);
        second.Complete(); await playing.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10));
        playing.Exit.TrySetResult(true); await f.Voice.Player.NextAsync();
        Assert.Equal(2, f.Voice.Engine.All.Count);
    }
    /// <summary>同canonical别名改变需先核对实际路由，不能废弃正在合成的同句。</summary>
    [Fact] public async Task CanonicalAliasChangePreservesInFlightSynthesis()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("same", "甘雨");
        var first = await f.Voice.Engine.NextAsync();
        var classified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Session.Event += e => { if (e.SourceSequence == 4 && e.Kind is ContinuousFrameEventKind.Discarded
            or ContinuousFrameEventKind.CandidateAccepted) classified.TrySetResult(); };
        await f.Frame(3, 1400); await f.Frame(4, 2400);
        (await f.Ocr.NextAsync()).Complete("same", "测试别名");
        await classified.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(first.Token.IsCancellationRequested);
        Assert.Equal(1, f.Session.Snapshot.SemanticGeneration);
        Assert.Single(f.Voice.Engine.All);
    }
    /// <summary>别名预检保留最初接受的句末标点，后续真实语气变化仍能及时换句。</summary>
    [Fact] public async Task AliasRouteRecheckDoesNotRewriteAcceptedSentenceEnding()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("你好？", "甘雨");
        var first = await f.Voice.Engine.NextAsync();
        Assert.Single(f.Voice.Engine.All);
        await f.Frame(3, 1400); await f.Frame(4, 2400);
        (await f.Ocr.NextAsync()).Complete("你好", "测试别名");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 4);
        Assert.Single(f.Voice.Engine.All);
        Assert.False(first.Token.IsCancellationRequested);
        await f.Stable(5, 2600, 2);
        (await f.Ocr.NextAsync()).Complete("你好！", "测试别名");
        await f.EventAsync(ContinuousFrameEventKind.CandidateAccepted, 7);
        // 旧合成被取消后由 fake 完成一次，允许串行泵排空并进入新句。
        first.Complete();
        await f.Voice.Engine.NextAsync();
        Assert.Equal(2, f.Voice.Engine.All.Count);
    }
    /// <summary>无效分隔线的正Rows不能取代Selected时唯一有效的分隔线。</summary>
    [Fact] public async Task InvalidOtherSeparatorCannotConfirmDialogueClear()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        f.Inspect.InvalidOtherSeparatorEvidence = true;
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("same");
        var first = await f.Voice.Engine.NextAsync();
        f.Inspect.ClearDialogueEvidence = true; f.Inspect.DropSelectedSeparatorEvidence = true;
        await f.Frame(3, 500); await f.Frame(4, 700);
        Assert.False(first.Token.IsCancellationRequested);
        Assert.Equal(1, f.Session.Snapshot.SemanticGeneration);
        Assert.DoesNotContain(f.Events, e => e.Detail == "ConfirmedDialogueClear");
    }
    /// <summary>预检释放publication后视觉可继续推进；延迟路由结果必须被丢弃。</summary>
    [Fact] public async Task DelayedAliasRouteComparisonCannotPublishAfterVisualChange()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("same");
        var first = await f.Voice.Engine.NextAsync(); await f.Voice.Routing.NextAsync();
        f.Voice.Routing.Hold = true;
        await f.Frame(3, 1400); await f.Frame(4, 2400);
        (await f.Ocr.NextAsync()).Complete("same", "测试别名");
        var checking = await f.Voice.Routing.NextAsync();
        await f.Frame(5, 2500, 2);
        checking.Release.TrySetResult(true);
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 4);
        Assert.False(first.Token.IsCancellationRequested);
        Assert.Equal(1, f.Session.Snapshot.SemanticGeneration);
    }
    /// <summary>移植回归验证：ObservationReturnsOnlyAfterLeaseAndReservationAreReleased。</summary>
    [Fact] public async Task ObservationReturnsOnlyAfterLeaseAndReservationAreReleased()
    {
        await using var f = new ContinuousFrameFixture();
        ContinuousFrameFixture.ObservingVoice? voice = null;
        await f.StartAsync(wrapVoice: inner => voice = new(inner));
        f.Inspect.Hold = true;
        var observation = f.Frame(0, 0);
        var completedBeforeRelease = false;
        voice!.ObservationEnding = () => completedBeforeRelease = observation.IsCompleted;
        f.Inspect.Release.TrySetResult();
        await observation;
        Assert.False(completedBeforeRelease);
        Assert.Equal(0, f.Voice.Session.Snapshot.ObservationCount);
        await f.Frame(1, 200);
    }
    /// <summary>移植回归验证：ThrowingOcrCancellationCallbackDoesNotRejectTheNewObservation。</summary>
    [Fact] public async Task ThrowingOcrCancellationCallbackDoesNotRejectTheNewObservation()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); var old = await f.Ocr.NextAsync();
        using var callback = old.Token.Register(() => throw new InvalidOperationException("controlled callback failure"));
        await f.Frame(3, 500, 2);
        Assert.True(old.Token.IsCancellationRequested);
        Assert.Equal(2, f.Session.Snapshot.Revision);
        Assert.Equal(0, f.Voice.Session.Snapshot.Revision);
        old.Complete();
        await f.Session.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }
    /// <summary>移植回归验证：FailedVisualVersionIsStillObservedWithoutRepeatedInvalidation。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedVisualVersionIsStillObservedWithoutRepeatedInvalidation(bool emptyText)
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync(true);
        await f.Stable(0, 0); var failed = await f.Ocr.NextAsync();
        if (emptyText) failed.Complete(" "); else failed.Result.TrySetException(new InvalidOperationException("controlled OCR failure"));
        await f.EventAsync(emptyText ? ContinuousFrameEventKind.Discarded : ContinuousFrameEventKind.Failed, 2);
        await f.Frame(3, 1400);
        var revision = f.Session.Snapshot.Revision;
        await f.Frame(4, 2400);
        var recheck = await f.Ocr.NextAsync();
        if (emptyText) recheck.Complete(" "); else recheck.Result.TrySetException(new InvalidOperationException("same OCR failure"));
        await f.EventAsync(emptyText ? ContinuousFrameEventKind.Discarded : ContinuousFrameEventKind.Failed, 4);
        await f.Session.CompleteAsync();
        Assert.Equal(revision, f.Session.Snapshot.Revision);
        Assert.Equal(2, f.Events.Count(value => value.Kind == ContinuousFrameEventKind.OcrCompleted));
        Assert.DoesNotContain(f.Events, value => value.Kind == ContinuousFrameEventKind.CandidateAccepted);
    }
    /// <summary>移植回归验证：RecheckAfterNoTextCannotRetryTheSameFailedCandidate。</summary>
    [Fact] public async Task RecheckAfterNoTextCannotRetryTheSameFailedCandidate()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete();
        var attempt = await f.Voice.Engine.NextAsync();
        attempt.Result.TrySetException(new InvalidOperationException("controlled synthesis failure"));
        await f.Voice.EventAsync(ContinuousVoiceEventKind.CandidateFailed, 1);
        await f.Frame(3, 1400); await f.Frame(4, 2400);
        (await f.Ocr.NextAsync()).Complete(" ");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 4);
        await f.Frame(5, 3400); await f.Frame(6, 4400);
        (await f.Ocr.NextAsync()).Complete();
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 6);
        await f.Session.CompleteAsync();
        Assert.Single(f.Voice.Engine.All);
    }
    /// <summary>移植回归验证：DisposeBeforeStartIsIdempotentAndSessionCannotRestart。</summary>
    [Fact] public async Task DisposeBeforeStartIsIdempotentAndSessionCannotRestart()
    {
        await using var f = new ContinuousFrameFixture();
        var session = new BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous.ContinuousFrameSession(ContinuousFrameGateTests.Settings, f.Inspect, f.Ocr);
        await session.DisposeAsync(); await session.DisposeAsync();
        await session.StopAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync());
    }
    /// <summary>移植回归验证：CancelledStartCanBeRetriedAndEventCanAsynchronouslyDispose。</summary>
    [Fact] public async Task CancelledStartCanBeRetriedAndEventCanAsynchronouslyDispose()
    {
        await using var f = new ContinuousFrameFixture();
        var session = new BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous.ContinuousFrameSession(ContinuousFrameGateTests.Settings, f.Inspect, f.Ocr);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.StartAsync(cancelled.Token));
        Task? disposal = null;
        session.Event += value => { if (value.Kind == ContinuousFrameEventKind.Started) disposal = session.DisposeAsync().AsTask(); };
        await session.StartAsync();
        Assert.NotNull(disposal);
        await disposal!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(session.Snapshot.Completed); Assert.True(session.Snapshot.Stopped);
    }
    /// <summary>移植回归验证：CopiesBeforeAsyncInspectionAndRejectsConcurrentSubmission。</summary>
    [Fact] public async Task CopiesBeforeAsyncInspectionAndRejectsConcurrentSubmission()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync(true); f.Inspect.Hold = true;
        var bytes = new byte[] { 1 }; var task = f.Session.ObserveAsync(0, TimeSpan.Zero, bytes); bytes[0] = 2;
        await f.Inspect.Reached.Task;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Frame(1, 100));
        Assert.Equal(1, f.Inspect.Last!.Bytes.Span[0]);
        f.Inspect.Release.TrySetResult(); await task;
    }
    /// <summary>移植回归验证：LatestPendingWinsAndNoncooperativeOcrCannotPublish。</summary>
    [Fact] public async Task LatestPendingWinsAndNoncooperativeOcrCannotPublish()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); var old = await f.Ocr.NextAsync();
        await f.Stable(3, 500, 2); await f.Stable(6, 1000, 3);
        Assert.True(old.Token.IsCancellationRequested); Assert.Single(f.Ocr.All);
        old.Complete("old"); var latest = await f.Ocr.NextAsync(); Assert.Equal(8, latest.Frame.Sequence);
        latest.Complete("latest"); var tts = await f.Voice.Engine.NextAsync(); Assert.Equal("latest", tts.Request.Text);
        Assert.Single(f.Voice.Engine.All);
    }
    /// <summary>移植回归验证：RechecksOfSameTextDoNotStarveSlowSynthesis。</summary>
    [Fact] public async Task RechecksOfSameTextDoNotStarveSlowSynthesis()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete(); var tts = await f.Voice.Engine.NextAsync();
        for (var n = 3; n < 12; n++)
        {
            await f.Frame(n, 400 + (n - 2) * 1000);
            if ((n - 2) % 2 == 0) (await f.Ocr.NextAsync()).Complete();
        }
        var completing = f.Session.CompleteAsync();
        Assert.False(tts.Token.IsCancellationRequested); tts.Complete();
        var playing = await f.Voice.Player.NextAsync(); playing.Exit.TrySetResult(true); await completing;
        Assert.Single(f.Voice.Engine.All);
    }
    /// <summary>移植回归验证：SignatureMissWithDifferentAcceptedTextAdvancesSemanticGeneration。</summary>
    [Fact] public async Task SignatureMissWithDifferentAcceptedTextAdvancesSemanticGeneration()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("A"); var a = await f.Voice.Engine.NextAsync();
        await f.Frame(3, 1400); await f.Frame(4, 2400); (await f.Ocr.NextAsync()).Complete("B");
        await f.Voice.EventAsync(ContinuousVoiceEventKind.CandidateOffered, 2);
        Assert.True(a.Token.IsCancellationRequested); a.Complete();
        var b = await f.Voice.Engine.NextAsync(); Assert.Equal("B", b.Request.Text);
    }
    /// <summary>移植回归验证：StopDuringHeldInspectionStopsVoiceImmediatelyAndDrainsInspection。</summary>
    [Fact] public async Task StopDuringHeldInspectionStopsVoiceImmediatelyAndDrainsInspection()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete(); (await f.Voice.Engine.NextAsync()).Complete();
        var a = await f.Voice.Player.NextAsync(); f.Inspect.Hold = true;
        var observation = f.Frame(3, 500, 2); var stop = f.Session.StopAsync();
        await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(stop.IsCompleted);
        a.Exit.TrySetResult(true); f.Inspect.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observation); await stop;
        Assert.Equal(0, f.Voice.Session.Snapshot.ObservationCount);
    }
    /// <summary>移植回归验证：CompleteUnstableEofDoesNotInventObservations。</summary>
    [Fact] public async Task CompleteUnstableEofDoesNotInventObservations()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync(true);
        await f.Frame(0, 0); await f.Session.CompleteAsync(); Assert.Empty(f.Ocr.All);
        Assert.Contains(f.Events, e => e.Kind == ContinuousFrameEventKind.Completed && e.Detail == "FinalFrameUnstable");
    }
    /// <summary>移植回归验证：RejectionJitterPreservesPlaybackAndSustainedLeavePermitsNewEpoch。</summary>
    [Fact] public async Task RejectionJitterPreservesPlaybackAndConfirmedExitPermitsNewEpoch()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete(); (await f.Voice.Engine.NextAsync()).Complete();
        var a = await f.Voice.Player.NextAsync();
        await f.Frame(3, 500, 0); Assert.False(a.Stop.Task.IsCompleted);
        await f.Frame(4, 1100, 0); Assert.False(a.Stop.Task.IsCompleted);
        await f.Session.ObserveAsync(5, TimeSpan.FromMilliseconds(1200), new byte[] { 0 }, dialogueExitConfirmed: true);
        await f.Session.ObserveAsync(6, TimeSpan.FromMilliseconds(1500), new byte[] { 0 }, dialogueExitConfirmed: true);
        await f.Session.ObserveAsync(7, TimeSpan.FromMilliseconds(1800), new byte[] { 0 }, dialogueExitConfirmed: true);
        await a.Stop.Task; Assert.Equal(1, f.Session.Snapshot.Epoch);
        await f.Stable(8, 1900); (await f.Ocr.NextAsync()).Complete();
        var second = await f.Voice.Engine.NextAsync(); Assert.Equal("hello", second.Request.Text);
        a.Exit.TrySetResult(true);
    }
    /// <summary>移植回归验证：OcrOnlyNeverTouchesVoiceAndWaitsForOcrAtEof。</summary>
    [Fact] public async Task OcrOnlyNeverTouchesVoiceAndWaitsForOcrAtEof()
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync(true);
        await f.Stable(0, 0); var ocr = await f.Ocr.NextAsync(); var completion = f.Session.CompleteAsync();
        Assert.False(completion.IsCompleted); ocr.Complete(); await completion;
        Assert.Contains(f.Events, e => e.Kind == ContinuousFrameEventKind.CandidateAccepted);
        Assert.Empty(f.Voice.Engine.All);
    }
}
