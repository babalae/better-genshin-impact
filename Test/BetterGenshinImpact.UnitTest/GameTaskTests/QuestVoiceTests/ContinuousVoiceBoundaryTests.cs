using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousVoiceBoundaryTests。</summary>
public sealed class ContinuousVoiceBoundaryTests
{
    /// <summary>移植回归验证：Rechecking current playing text deduplicates without cancelling playback。</summary>
    [Fact]
    public async Task Rechecking_current_playing_text_deduplicates_without_cancelling_playback()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync(); var a = await f.PlayAsync(1, "A");
        f.Offer(2, " A ", "测试别名"); await f.EventAsync(ContinuousVoiceEventKind.Duplicate, 2);
        Assert.False(a.Token.IsCancellationRequested); Assert.Single(f.Engine.All); Assert.Equal(0, f.Player.StopCount);
        a.Exit.SetResult(true); await f.Session.CompleteAsync();
    }

    /// <summary>移植回归验证：Cancelled unplayed same text retries on new revision。</summary>
    [Fact]
    public async Task Cancelled_unplayed_same_text_retries_on_new_revision()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        f.Offer(1, "A"); var first = await f.Engine.NextAsync(); f.Offer(2, "A"); first.Complete();
        var retry = await f.Engine.NextAsync(); Assert.Equal("A", retry.Request.Text); retry.Complete();
        var play = await f.Player.NextAsync(); play.Exit.SetResult(true); await f.Session.CompleteAsync();
        Assert.Equal(2, f.Engine.All.Count); Assert.Single(f.Player.All);
    }

    /// <summary>移植回归验证：Validation or save failure leaves A playing。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Validation_or_save_failure_leaves_A_playing(bool validation)
    {
        var wav = new SwitchableValidator();
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync(wav: wav); var a = await f.PlayAsync(1, "A");
        await f.Artifacts.NextAsync(); wav.Reject = validation; f.Artifacts.Hold = !validation;
        f.Offer(2, "B"); (await f.Engine.NextAsync()).Complete();
        if (!validation) (await f.Artifacts.NextAsync()).Result.SetException(new VoiceException(VoiceErrorCode.SaveFailed, "save failed"));
        await f.EventAsync(ContinuousVoiceEventKind.CandidateFailed, 2);
        Assert.False(a.Token.IsCancellationRequested); Assert.Equal(0, f.Player.StopCount); Assert.Single(f.Player.All);
        a.Exit.SetResult(true); await f.Session.CompleteAsync();
    }

    /// <summary>移植回归验证：Started callback can dispose before either pump runs。</summary>
    [Fact]
    public async Task Started_callback_can_dispose_before_either_pump_runs()
    {
        await using var f = new ContinuousVoiceFixture();
        await using var session = new ContinuousVoiceSession(f.Settings, f.Engine, new SwitchableValidator(), f.Artifacts, f.Player);
        Task? disposing = null;
        session.Event += e => { if (e.Kind == ContinuousVoiceEventKind.Started) disposing = session.DisposeAsync().AsTask(); };
        await session.StartAsync(); Assert.NotNull(disposing); await disposing!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(session.Snapshot.Completed); Assert.Empty(f.Engine.All);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync());
    }

    /// <summary>移植回归验证：Stale epoch and repeated revision do not enqueue and revision must increase。</summary>
    [Fact]
    public async Task Stale_epoch_and_repeated_revision_do_not_enqueue_and_revision_must_increase()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        f.Offer(1, "A"); var first = await f.Engine.NextAsync();
        Assert.Throws<ArgumentOutOfRangeException>(() => f.Session.Invalidate(1));
        f.Session.Offer(ContinuousVoiceFixture.Candidate(1, "other")); Assert.Null(f.Session.Snapshot.Pending);
        await f.Session.LeaveDialogueAsync(2);
        f.Session.Offer(ContinuousVoiceFixture.Candidate(2, "old epoch")); Assert.Null(f.Session.Snapshot.Pending);
        first.Complete(); await f.Session.CompleteAsync(); Assert.Single(f.Engine.All); Assert.Empty(f.Player.All);
    }

    /// <summary>移植回归验证：SwitchableValidator。</summary>
    private sealed class SwitchableValidator : IWavProcessor
    {
        /// <summary>移植回归验证：Reject。</summary>
        public bool Reject { get; set; }
        /// <summary>校验完整 PCM16 WAV 首部、长度、采样率和非静音样本。</summary>
        public WavAudio ValidateCompletedResponse(byte[] bytes) => Reject
            ? throw new VoiceException(VoiceErrorCode.InvalidAudio, "invalid") : new(bytes, 1, 32000, 1);
    }
}
