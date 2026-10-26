using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;

namespace BetterGenshinImpact.GameTask.QuestVoice;

/// <summary>串行管理启停并拥有全部在途观察；页面生命周期不参与资源所有权。</summary>
public sealed class QuestVoiceSessionController(IQuestVoiceSessionFactory factory) : IAsyncDisposable, IDisposable
{
    /// <summary>串行化创建、排空和释放，取消请求无需等待该锁。</summary>
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    /// <summary>保护帧预留和会话引用。</summary>
    private readonly object _gate = new();
    /// <summary>当前单轮会话。</summary>
    private IQuestVoiceSession? _session;
    /// <summary>本轮观察的取消源。</summary>
    private CancellationTokenSource? _lifetime;
    /// <summary>最后一个拥有的观察任务。</summary>
    private Task _observation = Task.CompletedTask;
    /// <summary>接收帧和在途帧标记。</summary>
    private bool _accepting, _observing, _disposed;
    /// <summary>单调帧序号和停止版本，避免排队启动越过停止。</summary>
    private long _sequence, _stopVersion;

    /// <summary>是否已经接受采样。</summary>
    public bool IsRunning { get { lock (_gate) return _accepting; } }
    /// <summary>是否仍有一帧在处理。</summary>
    public bool IsObserving { get { lock (_gate) return _observing; } }
    /// <summary>转发帧事件，不在 UI 线程同步等待。</summary>
    public event Action<ContinuousFrameEvent>? FrameEvent;
    /// <summary>转发配音事件。</summary>
    public event Action<ContinuousVoiceEvent>? VoiceEvent;
    /// <summary>捕获单帧异常；不会终止后续观察。</summary>
    public event Action<Exception>? ObservationFailed;

    /// <summary>重复启动只保留一个会话；停止过程中到达的旧启动会被取消。</summary>
    public async Task StartAsync(CancellationToken token = default)
    {
        var version = Interlocked.Read(ref _stopVersion);
        await _lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_accepting) return;
                if (version != Interlocked.Read(ref _stopVersion)) throw new OperationCanceledException();
                _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            }
            try
            {
                var session = await factory.CreateAsync(_lifetime.Token).ConfigureAwait(false);
                lock (_gate) _session = session;
                session.FrameEvent += ForwardFrame;
                session.VoiceEvent += ForwardVoice;
                await session.StartAsync(_lifetime.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    if (version != Interlocked.Read(ref _stopVersion)) throw new OperationCanceledException();
                    _sequence = 0;
                    _accepting = true;
                }
            }
            catch
            {
                await ReleaseSessionAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>最多一帧在途，忙时直接丢帧；同步复制缓冲区后才跨越线程。</summary>
    public bool TryObserve(ReadOnlyMemory<byte> bytes, TimeSpan at, bool dialogueExitConfirmed = false)
    {
        lock (_gate)
        {
            if (!_accepting || _observing || _session is null) return false;
            var owned = bytes.ToArray();
            var session = _session;
            var token = _lifetime!.Token;
            var sequence = ++_sequence;
            _observing = true;
            _observation = Task.Run(async () =>
            {
                try { await session.ObserveAsync(sequence, at, owned, token, dialogueExitConfirmed).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception exception) { ObservationFailed?.Invoke(exception); }
                finally { lock (_gate) _observing = false; }
            });
            return true;
        }
    }

    /// <summary>立即阻止采样并取消观察；等待后端串行排空，随后释放依赖。</summary>
    public async Task StopAsync(bool abortBackend = false)
    {
        Interlocked.Increment(ref _stopVersion);
        lock (_gate)
        {
            _accepting = false;
            _lifetime?.Cancel();
            if (abortBackend) _session?.AbortBackend();
        }
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await ReleaseSessionAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    /// <summary>先停止核心以立即取消播放，再排空观察，最终释放本轮依赖。</summary>
    private async Task ReleaseSessionAsync()
    {
        IQuestVoiceSession? session;
        Task observation;
        lock (_gate) { _accepting = false; session = _session; observation = _observation; }
        try
        {
            if (session is not null)
                await Task.WhenAll(session.StopAsync(), observation).ConfigureAwait(false);
            else await observation.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (session is not null)
                {
                    session.FrameEvent -= ForwardFrame;
                    session.VoiceEvent -= ForwardVoice;
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_gate)
                {
                    _session = null;
                    _lifetime?.Dispose();
                    _lifetime = null;
                }
            }
        }
    }

    /// <summary>事件只转发，UI 由宿主异步投递。</summary>
    private void ForwardFrame(ContinuousFrameEvent value) => FrameEvent?.Invoke(value);
    /// <summary>事件只转发，避免核心等待 UI Dispatcher。</summary>
    private void ForwardVoice(ContinuousVoiceEvent value) => VoiceEvent?.Invoke(value);
    /// <summary>允许重复释放，禁止之后重新启动。</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate) _disposed = true;
        await StopAsync(abortBackend: true).ConfigureAwait(false);
    }

    /// <summary>兼容 BetterGI 宿主的同步容器释放；正常退出已由 App.OnExit 异步排空。</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
