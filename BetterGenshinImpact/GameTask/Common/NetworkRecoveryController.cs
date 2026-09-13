using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
    private readonly Action<string>? _onInfo;
    private readonly Action<string>? _onWarning;
    private readonly TimeSpan _interval;
    private readonly Task _monitor;
    private string? _target;
    private int _failures;
    private bool _pending;
    private bool _healthy;
    private bool _recovering;
    private bool _stopping;
    private int _taskPauseWaiters;
    private int _taskPauseParticipants;
    private DateTimeOffset _retryAt;

    public static NetworkRecoveryController? Current => Volatile.Read(ref _current);
    public CancellationToken Token { get; }
    public bool IsRecoveryExecution => _inRecovery.Value;
    public bool IsPaused { get { lock (_sync) return _pending; } }
    public bool HasTaskPauseWaiter => Volatile.Read(ref _taskPauseWaiters) > 0;
    public bool IsTaskPauseAcknowledged
    {
        get
        {
            var waiters = Volatile.Read(ref _taskPauseWaiters);
            var participants = Volatile.Read(ref _taskPauseParticipants);
            return waiters > 0 && (participants == 0 || waiters >= participants);
        }
    }

    public NetworkRecoveryController(Func<string?> getTarget,
        Func<CancellationToken, Task<bool>> recover, CancellationToken token,
        Action<Exception>? onError = null,
        Action<string>? onInfo = null,
        Action<string>? onWarning = null,
        Func<string, CancellationToken, Task<bool>>? probe = null,
        TimeSpan? interval = null)
    {
        _getTarget = getTarget;
        _recover = recover;
        _onError = onError;
        _onInfo = onInfo;
        _onWarning = onWarning;
        _probe = probe ?? ((target, ct) => ProbeNetworkAsync(target, ct));
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

    /// <summary>登记同一任务内会并发发送输入的分支，恢复前必须等待这些分支全部进入暂停点。</summary>
    public IDisposable RegisterTaskPauseParticipants(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        Interlocked.Add(ref _taskPauseParticipants, count);
        return new Scope(() => Interlocked.Add(ref _taskPauseParticipants, -count));
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
                    var stopped = false;
                    lock (_sync)
                    {
                        stopped = _target is not null;
                        _target = null;
                        _failures = 0;
                        _healthy = false;
                        // 开关关闭不能让原任务与尚未结束的恢复流程抢操作权。
                        if (!_recovering) _pending = false;
                    }
                    if (stopped) _onInfo?.Invoke("网络健康监控已停止");
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
                        string? info = null;
                        string? warning = null;
                        lock (_sync)
                        {
                            if (!string.Equals(_target, target, StringComparison.OrdinalIgnoreCase))
                            {
                                _target = target;
                                _failures = 0;
                                _healthy = false;
                                info = $"网络健康监控已启动，探测目标：{target}";
                            }

                            var previousFailures = _failures;
                            var wasPending = _pending;
                            _healthy = healthy;
                            if (healthy)
                            {
                                _failures = 0;
                                if (previousFailures > 0)
                                {
                                    info = wasPending
                                        ? $"网络已恢复，探测目标：{target}，准备检查游戏状态"
                                        : $"网络连接已恢复，探测目标：{target}，未达到暂停阈值";
                                }
                            }
                            else
                            {
                                _failures = Math.Min(3, previousFailures + 1);
                                if (_failures != previousFailures)
                                {
                                    warning = _failures < 3
                                        ? $"网络探测失败（{_failures}/3），目标：{target}"
                                        : $"网络连续探测失败 3 次，已进入暂停等待恢复状态，目标：{target}";
                                }
                                if (_failures >= 3) _pending = true;
                            }
                        }
                        if (info is not null) _onInfo?.Invoke(info);
                        if (warning is not null) _onWarning?.Invoke(warning);
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
            _onInfo?.Invoke("网络恢复流程已启动，正在激活并检查游戏窗口");
            var succeeded = await _recover(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var resumed = false;
            lock (_sync)
            {
                // 恢复期间再次断网时保留暂停。
                if (succeeded && _healthy)
                {
                    _pending = false;
                    resumed = true;
                }
            }
            if (resumed) _onInfo?.Invoke("游戏状态恢复完成，已解除网络暂停");
            else if (!succeeded) _onWarning?.Invoke("暂未识别到可恢复的游戏界面，等待下一次检查");
            else _onWarning?.Invoke("恢复游戏期间网络再次不可用，继续保持暂停");
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

    /// <summary>ICMP 不可用时使用同一目标的常见 Web TCP 端口复核，避免仅因禁 Ping 永久误暂停。</summary>
    internal static async Task<bool> ProbeNetworkAsync(
        string target,
        CancellationToken ct,
        Func<string, CancellationToken, Task<bool>>? icmpProbe = null,
        Func<string, int, CancellationToken, Task<bool>>? tcpProbe = null,
        Func<bool>? isNetworkAvailable = null)
    {
        var host = NormalizeProbeTarget(target);
        icmpProbe ??= PingOnlyAsync;
        tcpProbe ??= TcpConnectAsync;
        isNetworkAvailable ??= NetworkInterface.GetIsNetworkAvailable;

        if (await icmpProbe(host, ct).ConfigureAwait(false)) return true;
        if (!isNetworkAvailable()) return false;

        // 复用用户配置的目标，不额外硬编码第三方站点。默认目标是 Web 主机，
        // 因此 443/80 任一可连接即可证明“只是 ICMP 被屏蔽”，不应暂停任务。
        var tcpResults = await Task.WhenAll(
            tcpProbe(host, 443, ct),
            tcpProbe(host, 80, ct)).ConfigureAwait(false);
        return tcpResults[0] || tcpResults[1];
    }

    private static string NormalizeProbeTarget(string target)
    {
        var trimmed = target.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : trimmed;
    }

    private static async Task<bool> PingOnlyAsync(string target, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            // 整体等待也有时限，避免域名解析拖住任务收尾。
            var result = await ping.SendPingAsync(target, 1500)
                .WaitAsync(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            return result.Status == IPStatus.Success;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PingException)
        {
            // 断网、DNS 解析失败等均属于正常的探测失败，由状态转换日志统一记录。
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task<bool> TcpConnectAsync(string target, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(target, port, ct).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            return client.Connected;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is SocketException or TimeoutException)
        {
            return false;
        }
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
