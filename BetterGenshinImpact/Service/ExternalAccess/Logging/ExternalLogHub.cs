using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BetterGenshinImpact.Service.ExternalAccess.Logging;

/// <summary>
/// 保存有限历史日志并向多个 WebSocket 客户端分发实时日志。
/// </summary>
public sealed class ExternalLogHub
{
    /// <summary>
    /// 新连接最多回放的历史日志数量。
    /// </summary>
    private const int ReplayCapacity = 200;

    /// <summary>
    /// 保护历史日志和订阅集合。
    /// </summary>
    private readonly object _locker = new();

    /// <summary>
    /// 最近产生的日志环形缓冲。
    /// </summary>
    private readonly Queue<ExternalLogEvent> _replayBuffer = new();

    /// <summary>
    /// 当前活动的客户端订阅。
    /// </summary>
    private readonly Dictionary<Guid, ExternalLogSubscription> _subscriptions = new();

    /// <summary>
    /// 进程内日志序号。
    /// </summary>
    private long _sequence;

    /// <summary>
    /// 将日志写入回放缓冲并非阻塞地投递给所有客户端。
    /// </summary>
    /// <param name="timestampUtc">日志 UTC 时间。</param>
    /// <param name="level">日志级别。</param>
    /// <param name="source">日志来源。</param>
    /// <param name="message">已渲染的日志正文。</param>
    /// <param name="exception">异常详情。</param>
    /// <param name="instance">BetterGI 实例标识。</param>
    public void Publish(
        DateTimeOffset timestampUtc,
        LogEventLevel level,
        string? source,
        string message,
        string? exception,
        string instance)
    {
        var logEvent = new ExternalLogEvent
        {
            Sequence = Interlocked.Increment(ref _sequence),
            TimestampUtc = timestampUtc,
            Level = level.ToString(),
            LevelValue = level,
            Source = source,
            Message = message,
            Exception = exception,
            Instance = instance
        };

        lock (_locker)
        {
            _replayBuffer.Enqueue(logEvent);
            while (_replayBuffer.Count > ReplayCapacity)
            {
                _replayBuffer.Dequeue();
            }

            foreach (var subscription in _subscriptions.Values)
            {
                if (level >= subscription.MinimumLevel)
                {
                    subscription.TryPublish(logEvent);
                }
            }
        }
    }

    /// <summary>
    /// 建立客户端订阅，并在同一临界区内取得历史快照，避免遗漏边界日志。
    /// </summary>
    /// <param name="minimumLevel">客户端要求的最低日志级别。</param>
    /// <returns>包含历史快照的实时日志订阅。</returns>
    public ExternalLogSubscription Subscribe(LogEventLevel minimumLevel)
    {
        lock (_locker)
        {
            var id = Guid.NewGuid();
            var snapshot = _replayBuffer.Where(item => item.LevelValue >= minimumLevel).ToArray();
            var subscription = new ExternalLogSubscription(id, minimumLevel, snapshot);
            _subscriptions.Add(id, subscription);
            return subscription;
        }
    }

    /// <summary>
    /// 移除并完成指定客户端订阅。
    /// </summary>
    /// <param name="subscriptionId">订阅唯一标识。</param>
    public void Unsubscribe(Guid subscriptionId)
    {
        ExternalLogSubscription? subscription;
        lock (_locker)
        {
            if (!_subscriptions.Remove(subscriptionId, out subscription))
            {
                return;
            }
        }

        subscription.Complete();
    }
}
