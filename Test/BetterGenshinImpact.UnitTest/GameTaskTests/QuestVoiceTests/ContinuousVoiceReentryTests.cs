using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousVoiceReentryTests。</summary>
public sealed class ContinuousVoiceReentryTests
{
    /// <summary>移植回归验证：Reentrant invalidation keeps each requested revision in event evidence。</summary>
    [Fact]
    public async Task Reentrant_invalidation_keeps_each_requested_revision_in_event_evidence()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        f.Offer(1, "A"); await f.Engine.NextAsync(); f.Offer(2, "B");
        f.Session.Event += e =>
        {
            if (e.Kind == ContinuousVoiceEventKind.Discarded && e.Revision == 2) f.Session.Invalidate(4);
        };
        f.Session.Invalidate(3);
        Assert.Single(f.Events, e => e.Kind == ContinuousVoiceEventKind.Invalidated && e.Revision == 3);
        Assert.Single(f.Events, e => e.Kind == ContinuousVoiceEventKind.Invalidated && e.Revision == 4);
    }

    /// <summary>移植回归验证：Throwing player stop retains error and still drains actual PlayAsync。</summary>
    [Fact]
    public async Task Throwing_player_stop_retains_error_and_still_drains_actual_PlayAsync()
    {
        var player = new ThrowingStopPlayer();
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync(player: player);
        f.Offer(1, "A"); (await f.Engine.NextAsync()).Complete();
        await f.EventAsync(ContinuousVoiceEventKind.PlaybackStarted, 1);
        try
        {
            var stop = f.Session.StopAsync();
            Assert.False(stop.IsCompleted); Assert.Equal(BetterGenshinImpact.GameTask.QuestVoice.Core.Tts.VoiceErrorCode.PlaybackDevice, f.Session.Snapshot.Error);
            player.Exit.TrySetResult(true); await stop.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { player.Exit.TrySetResult(true); }
    }

    /// <summary>移植回归验证：Observation opened during stop request holds ready audio after A exits。</summary>
    [Fact]
    public async Task Observation_opened_during_stop_request_holds_ready_audio_after_A_exits()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); IDisposable? observation = null;
        f.Session.Event += e => { if (e.Kind == ContinuousVoiceEventKind.StopRequested) observation = f.Session.BeginObservation(); };
        f.Offer(2, "B"); (await f.Engine.NextAsync()).Complete();
        await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10)); a.Exit.SetResult(true);
        await f.EventAsync(ContinuousVoiceEventKind.PlaybackCompleted, 1);
        Assert.Single(f.Player.All); Assert.NotNull(observation); Assert.Equal(1, f.Session.Snapshot.ObservationCount);
        observation!.Dispose(); observation.Dispose(); Assert.Equal("B", (await f.Player.NextAsync()).Text);
    }

    /// <summary>移植回归验证：Every subscriber observes monotonic sequence during synchronous stop reentry。</summary>
    [Fact]
    public async Task Every_subscriber_observes_monotonic_sequence_during_synchronous_stop_reentry()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var observed = new List<long>();
        f.Session.Event += e => { if (e.Kind == ContinuousVoiceEventKind.AudioReady) _ = f.Session.StopAsync(); };
        f.Session.Event += e => observed.Add(e.Sequence);
        f.Offer(1, "A"); (await f.Engine.NextAsync()).Complete();
        await f.EventAsync(ContinuousVoiceEventKind.AudioReady, 1); await f.Session.StopAsync();
        Assert.Equal(observed.Order(), observed); Assert.Equal(observed.Count, observed.Distinct().Count());
    }

    /// <summary>移植回归验证：Stop requested precedes actual completion when player finishes inline on cancellation。</summary>
    [Fact]
    public async Task Stop_requested_precedes_actual_completion_when_player_finishes_inline_on_cancellation()
    {
        var player = new InlinePlayer();
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync(player: player);
        f.Offer(1, "A"); (await f.Engine.NextAsync()).Complete();
        await f.EventAsync(ContinuousVoiceEventKind.PlaybackStarted, 1);
        await f.Session.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var events = f.Events.ToList();
        Assert.True(events.FindIndex(e => e.Kind == ContinuousVoiceEventKind.StopRequested)
            < events.FindIndex(e => e.Kind == ContinuousVoiceEventKind.PlaybackCompleted));
    }

    /// <summary>移植回归验证：Throwing cancellation callbacks do not skip player stop or synthesis drain。</summary>
    [Fact]
    public async Task Throwing_cancellation_callbacks_do_not_skip_player_stop_or_synthesis_drain()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        var a = await f.PlayAsync(1, "A"); f.Offer(2, "B"); var b = await f.Engine.NextAsync();
        using var registration = b.Token.Register(() => throw new InvalidOperationException("callback failure"));
        var stop = f.Session.StopAsync();
        Assert.True(a.Token.IsCancellationRequested); Assert.Equal(1, f.Player.StopCount); Assert.False(stop.IsCompleted);
        b.Complete(); a.Exit.SetResult(true); await stop.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>移植回归验证：Cancelled stop wait preserves ownership and disposal still drains。</summary>
    [Fact]
    public async Task Cancelled_stop_wait_preserves_ownership_and_disposal_still_drains()
    {
        await using var f = new ContinuousVoiceFixture(); await f.StartAsync();
        f.Offer(1, "A"); var a = await f.Engine.NextAsync(); using var cancellation = new CancellationTokenSource();
        var stop = f.Session.StopAsync(cancellation.Token); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);
        var dispose = f.Session.DisposeAsync().AsTask(); Assert.False(dispose.IsCompleted);
        a.Complete(); await dispose.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Empty(f.Player.All);
    }

    /// <summary>移植回归验证：InlinePlayer。</summary>
    private sealed class InlinePlayer : IAudioPlayer
    {
        /// <summary>移植回归验证： exit。</summary>
        private readonly TaskCompletionSource _exit = new();
        /// <summary>在工作线程使用默认共享输出播放完整 WAV，可取消并等待实际结束。</summary>
        public Task PlayAsync(byte[] wav, CancellationToken token)
        {
            token.Register(() => _exit.TrySetResult());
            return _exit.Task;
        }
        /// <summary>请求当前输出设备停止。</summary>
        public void Stop() => _exit.TrySetResult();
    }
    /// <summary>移植回归验证：ThrowingStopPlayer。</summary>
    private sealed class ThrowingStopPlayer : IAudioPlayer
    {
        /// <summary>移植回归验证：Exit。</summary>
        public TaskCompletionSource<bool> Exit { get; } = ContinuousVoiceFixture.NewCompletion<bool>();
        /// <summary>在工作线程使用默认共享输出播放完整 WAV，可取消并等待实际结束。</summary>
        public Task PlayAsync(byte[] wav, CancellationToken token) => Exit.Task;
        /// <summary>请求当前输出设备停止。</summary>
        public void Stop() => throw new InvalidOperationException("device stop failed");
    }
}
