using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notifier;

public class NotifierManager
{
    private readonly object _sync = new();
    private List<INotifier> _notifiers = [];

    private int _inFlight;
    private TaskCompletionSource? _drainTcs;
    private readonly object _drainLock = new();

    public static ILogger Logger { get; } = App.GetLogger<NotifierManager>();

    public NotifierManager()
    {
    }

    public void RegisterNotifier(INotifier notifier)
    {
        lock (_sync)
        {
            _notifiers.Add(notifier);
        }
    }

    public void RemoveNotifier<T>() where T : INotifier
    {
        List<INotifier> removed;
        lock (_sync)
        {
            removed = _notifiers.Where(o => o is T).ToList();
            _notifiers.RemoveAll(o => o is T);
        }

        WaitForDrain(TimeSpan.FromSeconds(3));
        foreach (var n in removed)
        {
            (n as IDisposable)?.Dispose();
        }
    }

    public void RemoveAllNotifiers()
    {
        List<INotifier> old;
        lock (_sync)
        {
            old = _notifiers;
            _notifiers = [];
        }

        // 等待在途发送结束，避免释放仍在使用的通知器资源
        WaitForDrain(TimeSpan.FromSeconds(3));

        foreach (var notifier in old)
        {
            (notifier as IDisposable)?.Dispose();
        }
    }

    public INotifier? GetNotifier<T>() where T : INotifier
    {
        lock (_sync)
        {
            return _notifiers.FirstOrDefault(o => o is T);
        }
    }

    public async Task SendNotificationAsync(INotifier notifier, BaseNotificationData content)
    {
        Interlocked.Increment(ref _inFlight);
        try
        {
            try
            {
                await notifier.SendAsync(content);
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning("{name} 通知发送失败: {ex}", notifier.Name, ex.Message);
            }
        }
        finally
        {
            if (Interlocked.Decrement(ref _inFlight) == 0)
            {
                TaskCompletionSource? tcs;
                lock (_drainLock)
                {
                    tcs = _drainTcs;
                    _drainTcs = null;
                }

                tcs?.TrySetResult();
            }
        }
    }

    public async Task SendNotificationAsync<T>(BaseNotificationData content) where T : INotifier
    {
        var notifier = GetNotifier<T>();
        if (notifier != null)
        {
            await SendNotificationAsync(notifier, content);
        }
    }

    public async Task SendNotificationToAllAsync(BaseNotificationData content)
    {
        INotifier[] snapshot;
        lock (_sync)
        {
            snapshot = _notifiers.ToArray();
        }

        await Task.WhenAll(snapshot.Select(notifier => SendNotificationAsync(notifier, content)));
    }

    private void WaitForDrain(TimeSpan timeout)
    {
        if (Volatile.Read(ref _inFlight) == 0)
            return;

        TaskCompletionSource tcs;
        lock (_drainLock)
        {
            _drainTcs ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs = _drainTcs;
        }

        tcs.Task.Wait(timeout);
    }
}
