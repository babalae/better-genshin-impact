using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace BetterGenshinImpact.Service.ExternalAccess.Logging;

/// <summary>
/// 一个 WebSocket 日志客户端的有界、非阻塞订阅。
/// </summary>
public sealed class ExternalLogSubscription
{
    /// <summary>
    /// 单客户端实时日志队列容量。
    /// </summary>
    private const int QueueCapacity = 512;

    /// <summary>
    /// 保护队列、完成状态和丢弃计数。
    /// </summary>
    private readonly object _locker = new();

    /// <summary>
    /// 等待中的实时日志队列。
    /// </summary>
    private readonly Queue<ExternalLogEvent> _queue = new();

    /// <summary>
    /// 表示当前可读取日志数量的异步信号量。
    /// </summary>
    private readonly SemaphoreSlim _available = new(0);

    /// <summary>
    /// 自上次通知以来被丢弃的日志数量。
    /// </summary>
    private long _droppedCount;

    /// <summary>
    /// 订阅是否已结束。
    /// </summary>
    private bool _completed;

    /// <summary>
    /// 创建一个日志客户端订阅。
    /// </summary>
    /// <param name="id">订阅唯一标识。</param>
    /// <param name="minimumLevel">客户端要求的最低日志级别。</param>
    /// <param name="snapshot">订阅建立时的历史日志快照。</param>
    internal ExternalLogSubscription(Guid id, LogEventLevel minimumLevel, IReadOnlyList<ExternalLogEvent> snapshot)
    {
        Id = id;
        MinimumLevel = minimumLevel;
        Snapshot = snapshot;
    }

    /// <summary>
    /// 订阅唯一标识。
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// 客户端要求的最低日志级别。
    /// </summary>
    public LogEventLevel MinimumLevel { get; }

    /// <summary>
    /// 建立订阅时需要首先发送的历史日志快照。
    /// </summary>
    public IReadOnlyList<ExternalLogEvent> Snapshot { get; }

    /// <summary>
    /// 非阻塞写入实时日志；队列已满时移除最旧项。
    /// </summary>
    /// <param name="logEvent">待写入的日志事件。</param>
    internal void TryPublish(ExternalLogEvent logEvent)
    {
        lock (_locker)
        {
            if (_completed)
            {
                return;
            }

            var shouldRelease = _queue.Count < QueueCapacity;
            if (!shouldRelease)
            {
                _queue.Dequeue();
                _droppedCount++;
            }

            _queue.Enqueue(logEvent);
            if (shouldRelease)
            {
                _available.Release();
            }
        }
    }

    /// <summary>
    /// 异步读取实时日志，直至订阅完成或请求被取消。
    /// </summary>
    /// <param name="cancellationToken">取消读取的令牌。</param>
    /// <returns>实时日志异步序列。</returns>
    public async IAsyncEnumerable<ExternalLogEvent> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);

            ExternalLogEvent? logEvent;
            lock (_locker)
            {
                if (_queue.Count > 0)
                {
                    logEvent = _queue.Dequeue();
                }
                else if (_completed)
                {
                    yield break;
                }
                else
                {
                    continue;
                }
            }

            yield return logEvent;
        }
    }

    /// <summary>
    /// 读取并清零尚未通知客户端的丢弃数量。
    /// </summary>
    /// <returns>累计丢弃数量。</returns>
    public long TakeDroppedCount()
    {
        lock (_locker)
        {
            var droppedCount = _droppedCount;
            _droppedCount = 0;
            return droppedCount;
        }
    }

    /// <summary>
    /// 完成订阅并唤醒等待中的读取者。
    /// </summary>
    internal void Complete()
    {
        lock (_locker)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _available.Release();
        }
    }
}
