using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// 默认事件总线实现。
/// 当前采用进程内同步分发，借由消费者内部自行决定是否异步缓冲。
/// </summary>
public sealed class GearTaskEventBus : IGearTaskEventBus
{
    private readonly ILogger<GearTaskEventBus> _logger;
    private readonly object _gate = new();
    private readonly List<IGearTaskEventConsumer> _consumers = [];

    public GearTaskEventBus(ILogger<GearTaskEventBus> logger)
    {
        _logger = logger;
    }

    public ValueTask PublishAsync(GearTaskExecutionEvent evt, CancellationToken ct = default)
    {
        IGearTaskEventConsumer[] snapshot;
        lock (_gate)
        {
            // 先拷贝快照，避免消费者在回调期间订阅或退订影响当前广播。
            snapshot = _consumers.ToArray();
        }

        return PublishCoreAsync(snapshot, evt, ct);
    }

    public IDisposable Subscribe(IGearTaskEventConsumer consumer)
    {
        lock (_gate)
        {
            _consumers.Add(consumer);
        }

        return new Subscription(this, consumer);
    }

    private async ValueTask PublishCoreAsync(IEnumerable<IGearTaskEventConsumer> consumers, GearTaskExecutionEvent evt, CancellationToken ct)
    {
        foreach (var consumer in consumers)
        {
            try
            {
                await consumer.ConsumeAsync(evt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GearTask 事件消费者处理失败，已隔离该异常。Consumer: {ConsumerType}, Event: {EventType}, RecordId: {RecordId}",
                    consumer.GetType().Name,
                    evt.GetType().Name,
                    evt.RecordId);
            }
        }
    }

    private void Unsubscribe(IGearTaskEventConsumer consumer)
    {
        lock (_gate)
        {
            _consumers.Remove(consumer);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly GearTaskEventBus _bus;
        private readonly IGearTaskEventConsumer _consumer;
        private bool _disposed;

        public Subscription(GearTaskEventBus bus, IGearTaskEventConsumer consumer)
        {
            _bus = bus;
            _consumer = consumer;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _bus.Unsubscribe(_consumer);
        }
    }
}
