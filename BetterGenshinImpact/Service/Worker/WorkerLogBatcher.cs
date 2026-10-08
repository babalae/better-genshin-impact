using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.Worker;

/// <summary>
/// 日志行批次发送器：按时间间隔或条数上限把日志行合并成一批再发送。
/// <para>
/// Worker 的日志频率在跑任务时可达每秒数十条，逐条写管道或逐条发通知都会被限流，
/// 因此统一在这里聚合。调用方只需 <see cref="Add"/>，线程安全且不会抛异常。
/// </para>
/// </summary>
internal sealed class WorkerLogBatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly List<string> _pending = [];
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private readonly Func<IReadOnlyList<string>, Task> _flushAsync;
    private readonly int _maxLines;
    private readonly Timer? _timer;
    private bool _disposed;

    /// <param name="interval">定时刷新间隔；小于等于零表示只按条数上限刷新</param>
    /// <param name="maxLines">达到该条数立即刷新</param>
    /// <param name="flushAsync">批次发送逻辑，内部自行处理异常</param>
    public WorkerLogBatcher(TimeSpan interval, int maxLines, Func<IReadOnlyList<string>, Task> flushAsync)
    {
        _flushAsync = flushAsync;
        _maxLines = Math.Max(1, maxLines);
        if (interval > TimeSpan.Zero)
        {
            _timer = new Timer(_ => _ = FlushAsync(), null, interval, interval);
        }
    }

    public void Add(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        bool shouldFlush;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending.Add(line);
            shouldFlush = _pending.Count >= _maxLines;
        }

        if (shouldFlush)
        {
            _ = FlushAsync();
        }
    }

    /// <summary>
    /// 立即发送已累积的日志行；没有待发送内容时直接返回
    /// </summary>
    public async Task FlushAsync()
    {
        await _flushSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            IReadOnlyList<string> batch;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                batch = _pending.ToArray();
                _pending.Clear();
            }

            await _flushAsync(batch).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 发送失败不能影响日志调用链（日志行已经落到文件日志里）
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending.Clear();
        }

        _timer?.Dispose();
        // 不释放 _flushSemaphore：定时器回调可能正在使用它，SemaphoreSlim 不依赖 OS 句柄，
        // 交给 GC 即可，避免在途刷新抛 ObjectDisposedException
    }
}
