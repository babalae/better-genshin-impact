using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousFrameFixture。</summary>
internal sealed class ContinuousFrameFixture : IAsyncDisposable
{
    /// <summary>移植回归验证：Voice。</summary>
    public ContinuousVoiceFixture Voice { get; } = new();
    /// <summary>移植回归验证：Inspect。</summary>
    public Inspector Inspect { get; } = new();
    /// <summary>移植回归验证：Ocr。</summary>
    public Recognizer Ocr { get; } = new();
    /// <summary>移植回归验证：Session。</summary>
    public ContinuousFrameSession Session { get; private set; } = null!;
    /// <summary>移植回归验证：Events。</summary>
    public ConcurrentQueue<ContinuousFrameEvent> Events { get; } = new();
    /// <summary>移植回归验证： events。</summary>
    private readonly ConcurrentDictionary<(ContinuousFrameEventKind, long), TaskCompletionSource<ContinuousFrameEvent>> _events = new();
    /// <summary>启动当前单轮管线；重复调用沿用既有启动任务。</summary>
    public async Task StartAsync(bool ocrOnly = false, Func<IContinuousVoiceSession, IContinuousVoiceSession>? wrapVoice = null)
    {
        if (!ocrOnly) await Voice.StartAsync();
        Session = new(ContinuousFrameGateTests.Settings, Inspect, Ocr, ocrOnly ? null : wrapVoice?.Invoke(Voice.Session) ?? Voice.Session);
        Session.Event += value =>
        {
            Events.Enqueue(value);
            if (value.SourceSequence is { } sequence)
                _events.GetOrAdd((value.Kind, sequence), _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(value);
        };
        await Session.StartAsync();
    }
    /// <summary>移植回归验证：Frame。</summary>
    public Task Frame(long sequence, int ms, int content = 1) => Session.ObserveAsync(sequence, TimeSpan.FromMilliseconds(ms), new byte[] { (byte)content });
    /// <summary>移植回归验证：EventAsync。</summary>
    public Task<ContinuousFrameEvent> EventAsync(ContinuousFrameEventKind kind, long sequence) =>
        _events.GetOrAdd((kind, sequence), _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(TimeSpan.FromSeconds(10));
    /// <summary>移植回归验证：Stable。</summary>
    public async Task Stable(long first, int ms, int content = 1)
    { await Frame(first, ms, content); await Frame(first + 1, ms + 200, content); await Frame(first + 2, ms + 400, content); }
    /// <summary>异步排空拥有的任务，再释放本轮资源。</summary>
    public async ValueTask DisposeAsync()
    {
        Inspect.Release.TrySetResult(); Ocr.ReleaseAll(); Voice.Engine.ReleaseAll(); Voice.Player.ReleaseAll();
        if (Session is not null) await Session.DisposeAsync();
        await Voice.DisposeAsync();
    }
    /// <summary>移植回归验证：Inspector。</summary>
    public sealed class Inspector : IContinuousFrameInspector
    {
        /// <summary>移植回归验证：Hold。</summary>
        public bool Hold { get; set; }
        /// <summary>在真实帧状态机测试中注入未知布局，不替代播放或候选逻辑。</summary>
        public SceneDecisionKind? NonSelectedKind { get; set; }
        /// <summary>注入来自真实selector同键的可靠清空证据。</summary>
        public bool ClearDialogueEvidence { get; set; }
        /// <summary>模拟另一分隔线有像素但超过厚度上界，原Selected仍只有standard有效。</summary>
        public bool InvalidOtherSeparatorEvidence { get; set; }
        /// <summary>在清空帧移除原来唯一有效的分隔线。</summary>
        public bool DropSelectedSeparatorEvidence { get; set; }
        /// <summary>未知布局的诊断原因。</summary>
        public string UnknownReason { get; set; } = "UnsupportedTextGeometry";
        /// <summary>移植回归验证：Reached。</summary>
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>移植回归验证：Release。</summary>
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>移植回归验证：Last。</summary>
        public ContinuousFrameSnapshot? Last { get; private set; }
        /// <summary>读取场景并量测对白区域墨迹。</summary>
        public async Task<ContinuousFrameInspection> InspectAsync(ContinuousFrameSnapshot frame, CancellationToken token)
        {
            Last = frame; Reached.TrySetResult();
            if (Hold) await Release.Task;
            var result = ContinuousFrameGateTests.Inspection(identity: frame.Bytes.Span[0].ToString(), selected: frame.Bytes.Span[0] != 0);
            // 已选中帧的证据来自相同catalog；清空只改变正文，保留可信名字和分隔线。
            if (result.Selection.Decision.Kind == SceneDecisionKind.Selected || ClearDialogueEvidence)
            {
                var evidence = new Dictionary<string, double> { ["NamePixels"] = 200, ["NameTallComponents"] = 2,
                    ["StandardSeparator.Rows"] = DropSelectedSeparatorEvidence ? 0 : 2, ["StandardSeparator.SideRun"] = 200,
                    ["TitleSeparator.Rows"] = InvalidOtherSeparatorEvidence ? 10 : 0, ["TitleSeparator.SideRun"] = 200,
                    ["TextPixels"] = ClearDialogueEvidence ? 0 : 100, ["TextComponents"] = ClearDialogueEvidence ? 0 : 5 };
                result = result with { Selection = result.Selection with { Decision = result.Selection.Decision with { Evidence = evidence } } };
                if (ClearDialogueEvidence) result = result with { Signature = null, Selection = result.Selection with
                    { Profile = null, Decision = result.Selection.Decision with
                        { Kind = SceneDecisionKind.Rejected, Reason = "NoSupportedDialogueFrame", LayoutId = null } } };
            }
            return NonSelectedKind is { } kind ? result with { Signature = null, Selection = result.Selection with
                { Profile = null, Decision = result.Selection.Decision with { Kind = kind, Reason = UnknownReason } } } : result;
        }
    }
    /// <summary>移植回归验证：Recognition。</summary>
    public sealed record Recognition(ContinuousFrameSnapshot Frame, CancellationToken Token)
    {
        /// <summary>移植回归验证：Result。</summary>
        public TaskCompletionSource<ImageOcrResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>移植回归验证：Complete。</summary>
        public void Complete(string text = "hello", string? name = "甘雨") => Result.TrySetResult(new(1920, 1080,
            new(1920, 1080, []), [], name, text, new("model", "hash", "controlled", 0, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }
    /// <summary>移植回归验证：Recognizer。</summary>
    public sealed class Recognizer : ContinuousVoiceFixture.Calls<Recognition>, IContinuousFrameRecognizer
    {
        /// <summary>移植回归验证： release。</summary>
        private bool _release;
        /// <summary>在取消检查和配置约束下执行识别。</summary>
        public Task<ImageOcrResult> RecognizeAsync(ContinuousFrameSnapshot frame, ContinuousFrameInspection inspection, CancellationToken token)
        { var call = new Recognition(frame, token); Add(call); if (_release) call.Complete(); return call.Result.Task; }
        /// <summary>移植回归验证：ReleaseAll。</summary>
        public void ReleaseAll() { _release = true; foreach (var call in All) call.Complete(); }
    }
    /// <summary>移植回归验证：ObservingVoice。</summary>
    public sealed class ObservingVoice(IContinuousVoiceSession inner) : IContinuousVoiceSession
    {
        /// <summary>移植回归验证：ObservationEnding。</summary>
        public Action? ObservationEnding { get; set; }
        /// <summary>读取当前不可变状态快照。</summary>
        public ContinuousVoiceSnapshot Snapshot => inner.Snapshot;
        /// <summary>按发生顺序发布的业务事件。</summary>
        public event Action<ContinuousVoiceEvent>? Event { add => inner.Event += value; remove => inner.Event -= value; }
        /// <summary>启动当前单轮管线；重复调用沿用既有启动任务。</summary>
        public Task StartAsync(CancellationToken token = default) => inner.StartAsync(token);
        /// <summary>递增视觉版本并丢弃尚未播放的旧内容。</summary>
        public void Invalidate(long revision) => inner.Invalidate(revision);
        /// <summary>只接受当前版本与周期中尚未提交的对白候选。</summary>
        public void Offer(ContinuousDialogueCandidate candidate) => inner.Offer(candidate);
        /// <summary>转发真实路由预检，包装器不能静默退化为“姓名一变即新句”。</summary>
        public Task<bool> MatchesCurrentDialogueAsync(ContinuousDialogueCandidate candidate, CancellationToken token = default)
            => inner.MatchesCurrentDialogueAsync(candidate, token);
        /// <summary>离开对白时切换周期，停止播放并等待其实际退出。</summary>
        public Task LeaveDialogueAsync(long revision) => inner.LeaveDialogueAsync(revision);
        /// <summary>取得观察租约，在画面版本判定之前阻止新播放。</summary>
        public IDisposable BeginObservation() => new Lease(inner.BeginObservation(), () => ObservationEnding?.Invoke());
        /// <summary>关闭输入后自然排空最新有效候选与播放。</summary>
        public Task CompleteAsync(CancellationToken token = default) => inner.CompleteAsync(token);
        /// <summary>拒绝后续工作、取消未播内容，并等待已拥有的任务排空。</summary>
        public Task StopAsync(CancellationToken token = default) => inner.StopAsync(token);
        /// <summary>异步排空拥有的任务，再释放本轮资源。</summary>
        public ValueTask DisposeAsync() => inner.DisposeAsync();
        /// <summary>移植回归验证：Lease。</summary>
        private sealed class Lease(IDisposable inner, Action ending) : IDisposable
        {
            /// <summary>释放本实例拥有的本地资源。</summary>
            public void Dispose() { try { ending(); } finally { inner.Dispose(); } }
        }
    }
}
