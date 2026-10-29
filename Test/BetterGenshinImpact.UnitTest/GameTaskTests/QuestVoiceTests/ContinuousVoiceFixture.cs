using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading.Channels;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousVoiceFixture。</summary>
internal sealed class ContinuousVoiceFixture : IAsyncDisposable
{
    /// <summary>移植回归验证：Catalog。</summary>
    public RoutingFixture Catalog { get; } = new();
    /// <summary>移植回归验证：Engine。</summary>
    public Speech Engine { get; } = new();
    /// <summary>移植回归验证：Artifacts。</summary>
    public Store Artifacts { get; } = new();
    /// <summary>移植回归验证：Player。</summary>
    public Audio Player { get; } = new();
    /// <summary>移植回归验证：Routing。</summary>
    public Resolver Routing { get; } = new();
    /// <summary>移植回归验证：Events。</summary>
    public ConcurrentQueue<ContinuousVoiceEvent> Events { get; } = new();
    /// <summary>移植回归验证： events。</summary>
    private readonly ConcurrentDictionary<(ContinuousVoiceEventKind, long), TaskCompletionSource<ContinuousVoiceEvent>> _events = new();
    /// <summary>移植回归验证：Session。</summary>
    public ContinuousVoiceSession Session { get; private set; } = null!;
    /// <summary>移植回归验证：Settings。</summary>
    public AppSettings Settings => new(Catalog.Root, new("http://localhost:19001/"), new(1, 20, 20), Catalog.LegacyVoice,
        new(Path.Combine(Catalog.Root, "logs"), Path.Combine(Catalog.Root, "audio")), new(900, 800), Routing: new(Catalog.CatalogPath));

    /// <summary>启动当前单轮管线；重复调用沿用既有启动任务。</summary>
    public async Task StartAsync(ITtsEngine? engine = null, IWavProcessor? wav = null, AppSettings? settings = null,
        IAudioPlayer? player = null)
    {
        await Catalog.WriteAsync();
        Session = new(settings ?? Settings, engine ?? Engine, wav ?? new Validator(), Artifacts, player ?? Player, Routing);
        Session.Event += value =>
        {
            Events.Enqueue(value);
            _events.GetOrAdd((value.Kind, value.Revision), _ => NewCompletion<ContinuousVoiceEvent>()).TrySetResult(value);
        };
        await Session.StartAsync();
    }

    /// <summary>移植回归验证：Candidate。</summary>
    public static ContinuousDialogueCandidate Candidate(long revision, string text, string name = "甘雨", long epoch = 0) =>
        new(revision, epoch, revision, TimeSpan.FromMilliseconds(revision), "sha", "dialogue", name, SpeakerNameStatus.Accepted, text);

    /// <summary>只接受当前版本与周期中尚未提交的对白候选。</summary>
    public void Offer(long revision, string text, string name = "甘雨", long? epoch = null)
    {
        Session.Invalidate(revision);
        Session.Offer(Candidate(revision, text, name, epoch ?? Session.Snapshot.Epoch));
    }

    /// <summary>移植回归验证：EventAsync。</summary>
    public Task<ContinuousVoiceEvent> EventAsync(ContinuousVoiceEventKind kind, long revision) =>
        _events.GetOrAdd((kind, revision), _ => NewCompletion<ContinuousVoiceEvent>()).Task.WaitAsync(TimeSpan.FromSeconds(10));

    /// <summary>在工作线程使用默认共享输出播放完整 WAV，可取消并等待实际结束。</summary>
    public async Task<Playback> PlayAsync(long revision, string text, string name = "甘雨")
    {
        Offer(revision, text, name);
        var synthesis = await Engine.NextAsync();
        synthesis.Complete();
        return await Player.NextAsync();
    }

    /// <summary>异步排空拥有的任务，再释放本轮资源。</summary>
    public async ValueTask DisposeAsync()
    {
        Engine.ReleaseAll(); Artifacts.ReleaseAll(); Routing.ReleaseAll(); Player.ReleaseAll();
        if (Session is not null) await Session.DisposeAsync();
        Catalog.Dispose();
    }

    /// <summary>移植回归验证：NewCompletion。</summary>
    public static TaskCompletionSource<T> NewCompletion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>移植回归验证：Calls。</summary>
    public abstract class Calls<T>
    {
        /// <summary>移植回归验证： calls。</summary>
        private readonly Channel<T> _calls = Channel.CreateUnbounded<T>();
        /// <summary>移植回归验证：All。</summary>
        public ConcurrentQueue<T> All { get; } = new();
        /// <summary>移植回归验证：Add。</summary>
        protected void Add(T call) { All.Enqueue(call); _calls.Writer.TryWrite(call); }
        /// <summary>移植回归验证：NextAsync。</summary>
        public Task<T> NextAsync() => _calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>移植回归验证：Synthesis。</summary>
    internal sealed class Synthesis(TtsRequest request, CancellationToken token)
    {
        /// <summary>移植回归验证：Request。</summary>
        public TtsRequest Request { get; } = request;
        /// <summary>移植回归验证：Token。</summary>
        public CancellationToken Token { get; } = token;
        /// <summary>移植回归验证：Result。</summary>
        public TaskCompletionSource<TtsResult> Result { get; } = NewCompletion<TtsResult>();
        /// <summary>移植回归验证：Complete。</summary>
        public void Complete() => Result.TrySetResult(new(System.Text.Encoding.UTF8.GetBytes(Request.Text), .1, .01, 1, 32000, 1, ReferenceFallback.None));
    }
    /// <summary>移植回归验证：Speech。</summary>
    internal sealed class Speech : Calls<Synthesis>, ITtsEngine
    {
        /// <summary>移植回归验证：Shutdowns。</summary>
        public int Shutdowns { get; private set; }
        /// <summary>移植回归验证： release。</summary>
        private bool _release;
        /// <summary>串行切换声线权重并读取完整语音响应，取消后丢弃结果。</summary>
        public Task<TtsResult> SynthesizeAsync(AppSettings settings, TtsRequest request, IProgress<TtsPhase>? progress, CancellationToken cancellationToken)
        {
            var call = new Synthesis(request, cancellationToken); Add(call);
            if (_release) call.Complete();
            return call.Result.Task;
        }
        /// <summary>移植回归验证：ReleaseAll。</summary>
        public void ReleaseAll() { _release = true; foreach (var call in All) call.Complete(); }
        /// <summary>中断本地 HTTP 等待，不向后端发送关闭指令。</summary>
        public void Shutdown() => Shutdowns++;
    }
    /// <summary>移植回归验证：Save。</summary>
    internal sealed class Save(byte[] bytes, CancellationToken token)
    {
        /// <summary>移植回归验证：Bytes。</summary>
        public byte[] Bytes { get; } = bytes;
        /// <summary>移植回归验证：Token。</summary>
        public CancellationToken Token { get; } = token;
        /// <summary>移植回归验证：Result。</summary>
        public TaskCompletionSource<string> Result { get; } = NewCompletion<string>();
        /// <summary>移植回归验证：Complete。</summary>
        public void Complete() => Result.TrySetResult("saved.wav");
    }
    /// <summary>移植回归验证：Store。</summary>
    internal sealed class Store : Calls<Save>, IAudioArtifactStore
    {
        /// <summary>移植回归验证：Hold。</summary>
        public bool Hold { get; set; }
        /// <summary>将完整音频先写临时文件，再原子移动到本轮目录。</summary>
        public Task<string> SaveAsync(string directory, byte[] wav, CancellationToken cancellationToken)
        {
            var call = new Save(wav, cancellationToken); Add(call);
            if (!Hold) call.Complete();
            return call.Result.Task;
        }
        /// <summary>移植回归验证：ReleaseAll。</summary>
        public void ReleaseAll() { Hold = false; foreach (var call in All) call.Complete(); }
    }
    /// <summary>移植回归验证：Playback。</summary>
    internal sealed class Playback(string text, CancellationToken token)
    {
        /// <summary>移植回归验证：Text。</summary>
        public string Text { get; } = text;
        /// <summary>移植回归验证：Token。</summary>
        public CancellationToken Token { get; } = token;
        /// <summary>移植回归验证：Exit。</summary>
        public TaskCompletionSource<bool> Exit { get; } = NewCompletion<bool>();
        /// <summary>请求当前输出设备停止。</summary>
        public TaskCompletionSource<bool> Stop { get; } = NewCompletion<bool>();
    }
    /// <summary>移植回归验证：Audio。</summary>
    internal sealed class Audio : Calls<Playback>, IAudioPlayer
    {
        /// <summary>移植回归验证： current。</summary>
        private Playback? _current;
        /// <summary>移植回归验证： release。</summary>
        private bool _release;
        /// <summary>移植回归验证： active。</summary>
        private int _active;
        /// <summary>移植回归验证：MaximumActive。</summary>
        public int MaximumActive { get; private set; }
        /// <summary>移植回归验证：StopCount。</summary>
        public int StopCount { get; private set; }
        /// <summary>在工作线程使用默认共享输出播放完整 WAV，可取消并等待实际结束。</summary>
        public async Task PlayAsync(byte[] wav, CancellationToken cancellationToken)
        {
            var call = new Playback(System.Text.Encoding.UTF8.GetString(wav), cancellationToken);
            Assert.Null(_current);
            _current = call; MaximumActive = Math.Max(MaximumActive, Interlocked.Increment(ref _active)); Add(call);
            if (_release) call.Exit.TrySetResult(true);
            try { await call.Exit.Task; }
            finally { _current = null; Interlocked.Decrement(ref _active); }
        }
        /// <summary>请求当前输出设备停止。</summary>
        public void Stop() { StopCount++; _current?.Stop.TrySetResult(true); }
        /// <summary>移植回归验证：ReleaseAll。</summary>
        public void ReleaseAll() { _release = true; foreach (var call in All) call.Exit.TrySetResult(true); }
    }
    /// <summary>移植回归验证：Resolve。</summary>
    internal sealed class Resolve(VoiceRoutingRequest request, CancellationToken token)
    {
        /// <summary>移植回归验证：Request。</summary>
        public VoiceRoutingRequest Request { get; } = request;
        /// <summary>移植回归验证：Token。</summary>
        public CancellationToken Token { get; } = token;
        /// <summary>移植回归验证：Release。</summary>
        public TaskCompletionSource<bool> Release { get; } = NewCompletion<bool>();
    }
    /// <summary>移植回归验证：Resolver。</summary>
    internal sealed class Resolver : Calls<Resolve>, IVoiceRoutingService
    {
        /// <summary>移植回归验证：Hold。</summary>
        public bool Hold { get; set; }
        /// <summary>按接受姓名解析角色和实际声线，保留兜底原因。</summary>
        public async Task<VoiceRoutingResult> ResolveAsync(VoiceRoutingRequest request, CancellationToken cancellationToken)
        {
            var call = new Resolve(request, cancellationToken); Add(call);
            if (Hold) await call.Release.Task;
            return await new VoiceRoutingService(new SpeakerCatalogLoader(), new SpeakerRouter()).ResolveAsync(request, CancellationToken.None);
        }
        /// <summary>移植回归验证：ReleaseAll。</summary>
        public void ReleaseAll() { Hold = false; foreach (var call in All) call.Release.TrySetResult(true); }
    }
    /// <summary>移植回归验证：Validator。</summary>
    private sealed class Validator : IWavProcessor
    {
        /// <summary>校验完整 PCM16 WAV 首部、长度、采样率和非静音样本。</summary>
        public WavAudio ValidateCompletedResponse(byte[] bytes) => new(bytes, 1, 32000, 1);
    }
}
