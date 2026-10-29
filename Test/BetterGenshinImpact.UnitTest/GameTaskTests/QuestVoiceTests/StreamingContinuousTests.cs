using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>流式Ready进入既有连续对白状态机，覆盖接替、版本和资源所有权。</summary>
public sealed class StreamingContinuousTests
{
    /// <summary>B首PCM已可播但HTTP未EOF，也只能在A实际退出后接替一次。</summary>
    [Fact]
    public async Task FirstPlayableBReplacesAWithoutWaitingForEofOrOverlappingDevices()
    {
        await using var f = new ContinuousVoiceFixture(); var output = new Output();
        using var bBody = new StreamingTransportTests.GateBody(StreamingTransportTests.PcmWave()); var posts = 0;
        using var engine = StreamingTransportTests.Engine((_, _) => Task.FromResult(StreamingTransportTests.Audio(
            Interlocked.Increment(ref posts) == 2 ? bBody : new MemoryStream(StreamingTransportTests.PcmWave()))));
        await f.StartAsync(engine, new WavProcessor(), StreamingSettings(f), output);
        try
        {
            f.Offer(1, "A"); var a = await output.NextAsync();
            f.Offer(2, "B"); await bBody.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.DoesNotContain(f.Events, e => e.Kind == ContinuousVoiceEventKind.SynthesisCompleted && e.Revision == 2);
            Assert.Single(output.All); a.Exit.TrySetResult(true);
            var b = await output.NextAsync(); Assert.Equal(1, output.MaximumActive);
            bBody.Release.TrySetResult(false); await f.EventAsync(ContinuousVoiceEventKind.SynthesisCompleted, 2);
            b.Exit.TrySetResult(true); await f.Session.CompleteAsync();
            Assert.Equal(2, output.All.Count);
        }
        finally { bBody.Release.TrySetResult(false); output.ReleaseAll(); }
    }

    /// <summary>只收到B头部不能停A；C使B过期，迟到B也不能发布Ready或播放。</summary>
    [Fact]
    public async Task HeaderOnlyBDoesNotStopAAndLateBDoesNotPlayAfterC()
    {
        await using var f = new ContinuousVoiceFixture(); var output = new Output();
        using var bBody = new StreamingTransportTests.GateBody(StreamingTransportTests.EmptyHeader(), StreamingTransportTests.PcmWave()[44..]);
        var posts = 0;
        using var engine = StreamingTransportTests.Engine((_, _) => Task.FromResult(StreamingTransportTests.Audio(
            Interlocked.Increment(ref posts) == 2 ? bBody : new MemoryStream(StreamingTransportTests.PcmWave()))));
        await f.StartAsync(engine, new WavProcessor(), StreamingSettings(f), output);
        try
        {
            f.Offer(1, "A"); var a = await output.NextAsync();
            f.Offer(2, "B"); await bBody.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(a.Token.IsCancellationRequested); Assert.False(a.Stop.Task.IsCompleted);
            f.Offer(3, "C"); Assert.False(bBody.ReadToken.IsCancellationRequested); Assert.Equal(2, posts);
            bBody.Release.TrySetResult(false); await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.DoesNotContain(f.Events, e => e.Kind == ContinuousVoiceEventKind.PlaybackStarted && e.Revision == 2);
            a.Exit.TrySetResult(true); var c = await output.NextAsync();
            c.Exit.TrySetResult(true); await f.Session.CompleteAsync();
            Assert.Contains(f.Events, e => e.Kind == ContinuousVoiceEventKind.PlaybackStarted && e.Revision == 3);
            Assert.Equal(2, output.All.Count);
        }
        finally { bBody.Release.TrySetResult(false); output.ReleaseAll(); }
    }

    /// <summary>当前A仍在流式合成时新B到来，不能因候选取消立即切断A。</summary>
    [Fact]
    public async Task NewCandidateDoesNotCancelAlreadyPlayingStreamBeforeReplacementReady()
    {
        await using var f = new ContinuousVoiceFixture(); var output = new Output();
        using var aBody = new StreamingTransportTests.GateBody(StreamingTransportTests.PcmWave());
        using var bBody = new StreamingTransportTests.GateBody(StreamingTransportTests.EmptyHeader(), StreamingTransportTests.PcmWave()[44..]);
        var posts = 0;
        using var engine = StreamingTransportTests.Engine((_, _) => Task.FromResult(StreamingTransportTests.Audio(
            Interlocked.Increment(ref posts) == 1 ? aBody : bBody)));
        await f.StartAsync(engine, new WavProcessor(), StreamingSettings(f), output);
        try
        {
            f.Offer(1, "A"); var a = await output.NextAsync();
            await aBody.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10)); f.Offer(2, "B");
            Assert.False(a.Token.IsCancellationRequested); Assert.False(aBody.ReadToken.IsCancellationRequested);
            Assert.Equal(1, posts); aBody.Release.TrySetResult(false);
            await bBody.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(a.Token.IsCancellationRequested);
            using (var reader = a.Source.CreateReader()) Assert.Equal(2, reader.Read(new byte[2], 0, 2));
            bBody.Release.TrySetResult(false); await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10));
            a.Exit.TrySetResult(true); var b = await output.NextAsync(); b.Exit.TrySetResult(true);
            await f.Session.CompleteAsync(); Assert.Equal(1, output.MaximumActive);
        }
        finally { aBody.Release.TrySetResult(false); bBody.Release.TrySetResult(false); output.ReleaseAll(); }
    }

    /// <summary>停止立即停止设备但仍拥有HTTP直到正常EOF，之后无spool泄漏。</summary>
    [Fact]
    public async Task StopStopsLocalOutputButWaitsForHttpDrainAndReleasesSpool()
    {
        await using var f = new ContinuousVoiceFixture(); var output = new Output();
        using var body = new StreamingTransportTests.GateBody(StreamingTransportTests.PcmWave());
        using var engine = StreamingTransportTests.Engine((_, _) => Task.FromResult(StreamingTransportTests.Audio(body)));
        await f.StartAsync(engine, new WavProcessor(), StreamingSettings(f), output);
        try
        {
            f.Offer(1, "A"); var a = await output.NextAsync();
            var stopping = f.Session.StopAsync(); Assert.True(a.Token.IsCancellationRequested); Assert.False(stopping.IsCompleted);
            a.Exit.TrySetResult(true); Assert.False(body.ReadToken.IsCancellationRequested);
            body.Release.TrySetResult(false); await stopping.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(f.Session.Snapshot.BackendUnknown);
            Assert.Empty(Directory.GetFiles(f.Settings.Output.AudioDirectory, "*.stream.pcm"));
        }
        finally { body.Release.TrySetResult(false); output.ReleaseAll(); }
    }

    /// <summary>观察租约阻止新播放，流先完成也不能绕过视觉版本检查。</summary>
    [Fact]
    public async Task ObservationLeasePreventsPrematureStreamingPlayback()
    {
        await using var f = new ContinuousVoiceFixture(); var output = new Output();
        using var engine = StreamingTransportTests.Engine((_, _) => Task.FromResult(StreamingTransportTests.Audio(new MemoryStream(StreamingTransportTests.PcmWave()))));
        await f.StartAsync(engine, new WavProcessor(), StreamingSettings(f), output);
        try
        {
            using var lease = f.Session.BeginObservation();
            f.Offer(1, "A"); await f.EventAsync(ContinuousVoiceEventKind.AudioReady, 1);
            Assert.Empty(output.All);
            f.Offer(2, "B"); await f.EventAsync(ContinuousVoiceEventKind.AudioReady, 2);
            Assert.Empty(output.All); lease.Dispose();
            var b = await output.NextAsync(); b.Exit.TrySetResult(true); await f.Session.CompleteAsync();
            Assert.Single(output.All);
            Assert.DoesNotContain(f.Events, e => e.Kind == ContinuousVoiceEventKind.PlaybackStarted && e.Revision == 1);
        }
        finally { output.ReleaseAll(); }
    }

    /// <summary>测试启动快照启用流式；不更改其他对白参数。</summary>
    private static AppSettings StreamingSettings(ContinuousVoiceFixture f) => f.Settings with
    { Protocol = new(StreamPlayback: true) };

    /// <summary>已经开始播放的流断开后停止本句并冻结后续请求，不误报正常结束。</summary>
    [Fact]
    public async Task InterruptedPlayingStreamStopsAndBlocksPendingCandidate()
    {
        await using var f = new ContinuousVoiceFixture(); var output = new Output();
        using var body = new StreamingTransportTests.GateBody(StreamingTransportTests.PcmWave()); var posts = 0;
        using var engine = StreamingTransportTests.Engine((_, _) =>
        { Interlocked.Increment(ref posts); return Task.FromResult(StreamingTransportTests.Audio(body)); });
        await f.StartAsync(engine, new WavProcessor(), StreamingSettings(f), output);
        try
        {
            f.Offer(1, "A"); var a = await output.NextAsync(); f.Offer(2, "B");
            body.Release.TrySetResult(true); await f.EventAsync(ContinuousVoiceEventKind.CandidateFailed, 1);
            await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(f.Session.Snapshot.BackendUnknown); Assert.Null(f.Session.Snapshot.Pending); Assert.Equal(1, posts);
            a.Exit.TrySetResult(true); await f.Session.CompleteAsync();
            Assert.Single(output.All); Assert.Empty(Directory.GetFiles(f.Settings.Output.AudioDirectory, "*.stream.pcm"));
        }
        finally { body.Release.TrySetResult(true); output.ReleaseAll(); }
    }

    /// <summary>Ready事件中同步提交新版本，旧流引用立即释放而不干扰在途HTTP所有者。</summary>
    [Fact]
    public async Task ReentrantInvalidationAtStreamingReadyDiscardsOldLeaseWithoutLeaking()
    {
        await using var f = new ContinuousVoiceFixture(); var output = new Output();
        using var engine = StreamingTransportTests.Engine((_, _) => Task.FromResult(StreamingTransportTests.Audio(new MemoryStream(StreamingTransportTests.PcmWave()))));
        await f.StartAsync(engine, new WavProcessor(), StreamingSettings(f), output);
        f.Session.Event += value =>
        {
            if (value.Kind == ContinuousVoiceEventKind.AudioReady && value.Revision == 1) f.Offer(2, "B");
        };
        try
        {
            f.Offer(1, "A"); var b = await output.NextAsync(); b.Exit.TrySetResult(true); await f.Session.CompleteAsync();
            Assert.DoesNotContain(f.Events, e => e.Kind == ContinuousVoiceEventKind.PlaybackStarted && e.Revision == 1);
            Assert.Contains(f.Events, e => e.Kind == ContinuousVoiceEventKind.PlaybackStarted && e.Revision == 2);
            Assert.Single(output.All); Assert.Empty(Directory.GetFiles(f.Settings.Output.AudioDirectory, "*.stream.pcm"));
        }
        finally { output.ReleaseAll(); }
    }

    /// <summary>一次设备生命周期，取消与实际退出分别可控。</summary>
    private sealed record Play(StreamingPcmAudio Source, CancellationToken Token)
    {
        /// <summary>实际输出线程退出信号。</summary>
        public TaskCompletionSource<bool> Exit { get; } = ContinuousVoiceFixture.NewCompletion<bool>();
        /// <summary>收到本地停止请求。</summary>
        public TaskCompletionSource<bool> Stop { get; } = ContinuousVoiceFixture.NewCompletion<bool>();
    }

    /// <summary>不打开声卡的可控单设备播放器，验证会话调度本身。</summary>
    private sealed class Output : ContinuousVoiceFixture.Calls<Play>, IStreamingAudioPlayer
    {
        /// <summary>当前唯一设备所有者。</summary>
        private Play? _current;
        /// <summary>测试退出时自动完成迟到工作。</summary>
        private bool _release;
        /// <summary>检测重叠播放。</summary>
        private int _active;
        /// <summary>最大同时活跃设备数。</summary>
        public int MaximumActive { get; private set; }
        /// <summary>流式测试禁止静默退回整文件播放。</summary>
        public Task PlayAsync(byte[] wav, CancellationToken cancellationToken) => throw new InvalidOperationException("Expected incremental playback");
        /// <summary>开始单次增量播放，测试单独控制停止请求与实际退出。</summary>
        public async Task PlayStreamingAsync(StreamingPcmAudio source, CancellationToken cancellationToken)
        {
            var call = new Play(source, cancellationToken); Assert.Null(_current); _current = call;
            MaximumActive = Math.Max(MaximumActive, Interlocked.Increment(ref _active)); Add(call);
            using var registration = cancellationToken.Register(() => call.Stop.TrySetResult(true));
            if (_release) call.Exit.TrySetResult(true);
            try { await call.Exit.Task; }
            finally { _current = null; Interlocked.Decrement(ref _active); }
        }
        /// <summary>请求停止不假装已经退出。</summary>
        public void Stop() => _current?.Stop.TrySetResult(true);
        /// <summary>确保失败的测试也能排空夹具。</summary>
        public void ReleaseAll() { _release = true; foreach (var call in All) call.Exit.TrySetResult(true); }
    }
}
