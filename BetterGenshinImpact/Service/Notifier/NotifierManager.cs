using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// 通知器管理器。以「发送租约」协调通知器生命周期：
/// 每个在途发送在锁内获取通知器租约；移除/释放通知器前先原子替换集合，
/// 再等待所有租约归还，避免在发送中途释放通知器资源。
/// </summary>
public class NotifierManager
{
    private readonly object _sync = new();
    private List<INotifier> _notifiers = [];
    private readonly Dictionary<INotifier, int> _leases = new();
    private readonly ManualResetEventSlim _idle = new(true);

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
        INotifier[] removed;
        lock (_sync)
        {
            removed = _notifiers.Where(o => o is T).ToArray();
            _notifiers = _notifiers.Where(o => o is not T).ToList();
        }

        WaitForLeasesReleased();
        foreach (var n in removed)
        {
            (n as IDisposable)?.Dispose();
        }
    }

    public void RemoveAllNotifiers()
    {
        INotifier[] old;
        lock (_sync)
        {
            old = _notifiers.ToArray();
            _notifiers = [];
        }

        WaitForLeasesReleased();
        foreach (var n in old)
        {
            (n as IDisposable)?.Dispose();
        }
    }

    public INotifier? GetNotifier<T>() where T : INotifier
    {
        lock (_sync)
        {
            return _notifiers.FirstOrDefault(o => o is T);
        }
    }

    /// <summary>
    /// 以租约方式发送（在途计数，供普通通知与测试通知共用）。
    /// </summary>
    public async Task SendNotificationAsync(INotifier notifier, BaseNotificationData content)
    {
        var lease = TryAcquireLease(notifier);
        if (lease == null)
            return;

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
            ReleaseLease(lease);
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

        await Task.WhenAll(snapshot.Select(n => SendNotificationAsync(n, content)));
    }

    /// <summary>
    /// 测试指定通知器，返回异常信息供 UI 展示；测试发送同样持有租约，避免释放竞态。
    /// </summary>
    public async Task<string?> SendTestAsync<T>(BaseNotificationData content) where T : INotifier
    {
        var notifier = GetNotifier<T>();
        if (notifier == null)
            return "通知类型未启用";

        var lease = TryAcquireLease(notifier);
        if (lease == null)
            return "通知器正在被移除，请稍后重试";

        try
        {
            await notifier.SendAsync(content);
            return null;
        }
        catch (System.Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            ReleaseLease(lease);
        }
    }

    private Lease? TryAcquireLease(INotifier notifier)
    {
        lock (_sync)
        {
            if (!_notifiers.Contains(notifier))
                return null;

            _leases[notifier] = _leases.TryGetValue(notifier, out var c) ? c + 1 : 1;
            _idle.Reset();
            return new Lease(this, notifier);
        }
    }

    private void ReleaseLease(Lease lease)
    {
        lock (_sync)
        {
            if (!_leases.TryGetValue(lease.Notifier, out var count) || count <= 1)
            {
                _leases.Remove(lease.Notifier);
            }
            else
            {
                _leases[lease.Notifier] = count - 1;
            }

            if (_leases.Count == 0)
            {
                _idle.Set();
            }
        }
    }

    private void WaitForLeasesReleased()
    {
        if (_leases.Count == 0)
            return;
        _idle.Wait(TimeSpan.FromSeconds(3));
    }

    private sealed class Lease(NotifierManager owner, INotifier notifier)
    {
        public INotifier Notifier { get; } = notifier;
        public NotifierManager Owner { get; } = owner;
    }
}
