using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// 一次实时捕获会话的网络状态。后台独立探测；任务线程在检查点暂停，实时触发器负责恢复。
/// </summary>
public sealed class NetworkRecoveryController : IAsyncDisposable
{
    private static NetworkRecoveryController? _current;
    private readonly AsyncLocal<bool> _inRecovery = new();
    private readonly object _sync = new();
    private readonly SemaphoreSlim _recoveryGate = new(1, 1);
    private readonly CancellationTokenSource _stop;
    private readonly Func<string?> _getTarget;
    private readonly Func<string, CancellationToken, Task<bool>> _probe;
    private readonly Func<CancellationToken, Task<bool>> _recover;
    private readonly Action<Exception>? _onError;
    private readonly TimeSpan _interval;
    private readonly Task _monitor;
    private string? _target;
    private int _failures;
    private bool _pending;
    private bool _healthy;
    private bool _recovering;
    private bool _stopping;
    private int _taskPauseWaiters;
    private DateTimeOffset _retryAt;

    public static NetworkRecoveryController? Current => Volatile.Read(ref _current);
    public CancellationToken Token { get; }
    public bool IsRecoveryExecution => _inRecovery.Value;
    public bool IsPaused { get { lock (_sync) return _pending; } }
    public bool HasTaskPauseWaiter => Volatile.Read(ref _taskPauseWaiters) > 0;

    public NetworkRecoveryController(Func<string?> getTarget,
        Func<CancellationToken, Task<bool>> recover, CancellationToken token,
        Action<Exception>? onError = null,
        Func<string, CancellationToken, Task<bool>>? probe = null,
        TimeSpan? interval = null)
    {
        _getTarget = getTarget;
        _recover = recover;
        _onError = onError;
        _probe = probe ?? PingAsync;
        _interval = interval ?? TimeSpan.FromSeconds(5);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Token = _stop.Token;
        _monitor = Task.Run(MonitorAsync);
    }

    // 实时触发器持有此作用域，使任务线程与截图调度线程看到同一个会话。
    public IDisposable Enter()
    {
        var previous = Interlocked.Exchange(ref _current, this);
        return new Scope(() => Interlocked.CompareExchange(ref _current, previous, this));
    }

    public IDisposable AcknowledgeTaskPaused()
    {
        Interlocked.Increment(ref _taskPauseWaiters);
        return new Scope(() => Interlocked.Decrement(ref _taskPauseWaiters));
    }

    private async Task MonitorAsync()
    {
        try
        {
            while (true)
            {
                Token.ThrowIfCancellationRequested();
                var target = _getTarget()?.Trim();
                if (string.IsNullOrWhiteSpace(target))
                {
                    lock (_sync)
                    {
                        _target = null;
                        _failures = 0;
                        _healthy = false;
                        // 开关关闭不能让原任务与尚未结束的恢复流程抢操作权。
                        if (!_recovering) _pending = false;
                    }
                }
                else
                {
                    bool healthy;
                    try { healthy = await _probe(target, Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (Token.IsCancellationRequested) { throw; }
                    catch (Exception e) { _onError?.Invoke(e); healthy = false; }
                    Token.ThrowIfCancellationRequested();
                    // 探测期间改过配置，不把旧目标的结果计入新目标。
                    if (string.Equals(target, _getTarget()?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        lock (_sync)
                        {
                            if (!string.Equals(_target, target, StringComparison.OrdinalIgnoreCase))
                            {
                                _target = target;
                                _failures = 0;
                            }
                            _healthy = healthy;
                            _failures = healthy ? 0 : Math.Min(3, _failures + 1);
                            if (_failures >= 3) _pending = true;
                        }
                    }
                }
                await Task.Delay(_interval, Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (Token.IsCancellationRequested) { }
    }

    /// <summary>由实时触发器调用。成功验证游戏主界面后才解除任务暂停。</summary>
    public async Task TryRecoverAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(Token, cancellationToken);
        var ct = linked.Token;
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_stopping || !_pending || !_healthy || _recovering || DateTimeOffset.UtcNow < _retryAt) return;
            if (!_recoveryGate.Wait(0)) return;
            _recovering = true;
        }
        try
        {
            _inRecovery.Value = true;
            var succeeded = await _recover(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                // 恢复期间再次断网时保留暂停。
                if (succeeded && _healthy) _pending = false;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { _onError?.Invoke(e); }
        finally
        {
            _inRecovery.Value = false;
            lock (_sync)
            {
                _recovering = false;
                _retryAt = DateTimeOffset.UtcNow + _interval;
                _recoveryGate.Release();
            }
        }
    }

    private static async Task<bool> PingAsync(string target, CancellationToken ct)
    {
        using var ping = new Ping();
        // 整体等待也有时限，避免域名解析拖住任务收尾。
        var result = await ping.SendPingAsync(target, 1500)
            .WaitAsync(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        return result.Status == IPStatus.Success;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync) _stopping = true;
        try { _stop.Cancel(); }
        catch (Exception e) { _onError?.Invoke(e); }
        finally
        {
            try { await _monitor.ConfigureAwait(false); }
            finally
            {
                await _recoveryGate.WaitAsync().ConfigureAwait(false);
                _recoveryGate.Release();
                lock (_sync) _pending = false;
                _stop.Dispose();
            }
        }
    }

    private sealed class Scope(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
