using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BetterGenshinImpact.Core.Monitor;

public abstract class RelativeMouseInputMonitorBase(ILogger logger) : IRelativeMouseInputMonitor, IDisposable
{
    private readonly object _subscriptionLock = new();
    private readonly Dictionary<long, EventHandler<RelativeMouseMoveEventArgs>> _handlers = [];
    private long _nextSubscriptionId;
    private bool _isStarted;
    private bool _isStopping;
    private bool _isDisposed;

    protected ILogger Logger { get; } = logger;

    public IDisposable Subscribe(EventHandler<RelativeMouseMoveEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        long subscriptionId;
        lock (_subscriptionLock)
        {
            while (_isStopping)
            {
                System.Threading.Monitor.Wait(_subscriptionLock);
            }

            ObjectDisposedException.ThrowIf(_isDisposed, this);

            subscriptionId = ++_nextSubscriptionId;
            _handlers.Add(subscriptionId, handler);

            if (!_isStarted)
            {
                try
                {
                    StartCore();
                    _isStarted = true;
                }
                catch
                {
                    _handlers.Remove(subscriptionId);
                    throw;
                }
            }
        }

        return new Subscription(this, subscriptionId);
    }

    protected void Publish(RelativeMouseMoveEventArgs eventArgs)
    {
        EventHandler<RelativeMouseMoveEventArgs>[] handlers;
        lock (_subscriptionLock)
        {
            if (_isDisposed || _handlers.Count == 0)
            {
                return;
            }

            handlers = _handlers.Values.ToArray();
        }

        foreach (var handler in handlers)
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "相对鼠标移动事件订阅方执行失败");
            }
        }
    }

    protected abstract void StartCore();

    protected abstract void StopCore();

    public void Dispose()
    {
        bool shouldStop;
        lock (_subscriptionLock)
        {
            while (_isStopping)
            {
                System.Threading.Monitor.Wait(_subscriptionLock);
            }

            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _handlers.Clear();
            shouldStop = _isStarted;
            _isStarted = false;
            _isStopping = shouldStop;
        }

        if (shouldStop)
        {
            try
            {
                StopCore();
            }
            finally
            {
                CompleteStop();
            }
        }

        GC.SuppressFinalize(this);
    }

    private void Unsubscribe(long subscriptionId)
    {
        bool shouldStop;
        lock (_subscriptionLock)
        {
            if (_isDisposed || !_handlers.Remove(subscriptionId))
            {
                return;
            }

            shouldStop = _handlers.Count == 0 && _isStarted;
            if (shouldStop)
            {
                _isStarted = false;
                _isStopping = true;
            }
        }

        if (shouldStop)
        {
            try
            {
                StopCore();
            }
            finally
            {
                CompleteStop();
            }
        }
    }

    private void CompleteStop()
    {
        lock (_subscriptionLock)
        {
            _isStopping = false;
            System.Threading.Monitor.PulseAll(_subscriptionLock);
        }
    }

    private sealed class Subscription(RelativeMouseInputMonitorBase owner, long subscriptionId) : IDisposable
    {
        private RelativeMouseInputMonitorBase? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Unsubscribe(subscriptionId);
        }
    }
}
