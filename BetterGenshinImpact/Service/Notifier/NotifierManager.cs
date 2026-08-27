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
/// 每个在途发送在持锁取得快照时同步获取通知器租约；移除/释放通知器前先原子替换集合，
/// 再等待所有租约归还（无固定超时），避免在发送中途释放通知器资源。
/// </summary>
public class NotifierManager
{
    private readonly object _sync = new();
    private List<INotifier> _notifiers = [];
    private readonly Dictionary<INotifier, int> _leases = new();

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

        WaitForLeasesReleased(removed);
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

        WaitForLeasesReleased(old);
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
    /// 以租约方式发送单个通知器（在途计数，供普通通知路径使用）。
    /// </summary>
    public async Task SendNotificationAsync(INotifier notifier, BaseNotificationData content)
    {
        Lease? lease;
        lock (_sync)
        {
            lease = TryAcquireLeaseLocked(notifier);
        }

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
        INotifier? notifier;
        Lease? lease;
        lock (_sync)
        {
            notifier = _notifiers.FirstOrDefault(o => o is T);
            lease = notifier != null ? TryAcquireLeaseLocked(notifier) : null;
        }

        if (notifier == null || lease == null)
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

    /// <summary>
    /// 向所有通知器发送。在持锁取得快照时同步为每个通知器获取租约，
    /// 避免与 RemoveAllNotifiers 竞态导致快照中的实例被释放或无法获取租约。
    /// </summary>
    public async Task SendNotificationToAllAsync(BaseNotificationData content)
    {
        var leased = new List<(INotifier Notifier, Lease Lease)>();
        lock (_sync)
        {
            foreach (var notifier in _notifiers)
            {
                var lease = TryAcquireLeaseLocked(notifier);
                if (lease != null)
                    leased.Add((notifier, lease));
            }
        }

        try
        {
            await Task.WhenAll(leased.Select(item => SendWithLeaseAsync(item.Notifier, item.Lease, content)));
        }
        finally
        {
            foreach (var item in leased)
            {
                ReleaseLease(item.Lease);
            }
        }
    }

    /// <summary>
    /// 测试指定通知器，返回错误信息供 UI 展示；测试发送同样纳入租约计数。
    /// </summary>
    public async Task<string?> SendTestAsync<T>(BaseNotificationData content) where T : INotifier
    {
        INotifier? notifier;
        Lease? lease;
        lock (_sync)
        {
            notifier = _notifiers.FirstOrDefault(o => o is T);
            lease = notifier != null ? TryAcquireLeaseLocked(notifier) : null;
        }

        if (notifier == null)
            return "通知类型未启用";
        if (lease == null)
            return "通知器正在被移除，请稍后重试";

        try
        {
            try
            {
                await notifier.SendAsync(content);
                return null;
            }
            catch (System.Exception ex)
            {
                return ex.Message;
            }
        }
        finally
        {
            ReleaseLease(lease);
        }
    }

    private async Task SendWithLeaseAsync(INotifier notifier, Lease lease, BaseNotificationData content)
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

    /// <summary>
    /// 持有 _sync 时调用：若通知器仍注册则获取租约，否则返回 null。
    /// </summary>
    private Lease? TryAcquireLeaseLocked(INotifier notifier)
    {
        if (!_notifiers.Contains(notifier))
            return null;

        _leases[notifier] = _leases.TryGetValue(notifier, out var c) ? c + 1 : 1;
        return new Lease(this, notifier);
    }

    private void ReleaseLease(Lease lease)
    {
        lock (_sync)
        {
            if (_leases.TryGetValue(lease.Notifier, out var count))
            {
                if (count <= 1)
                    _leases.Remove(lease.Notifier);
                else
                    _leases[lease.Notifier] = count - 1;
            }

            // 唤醒等待租约释放的释放方
            Monitor.PulseAll(_sync);
        }
    }

    /// <summary>
    /// 等待给定通知器集合的全部租约归还后才继续（无固定超时）。
    /// </summary>
    private void WaitForLeasesReleased(INotifier[] notifiers)
    {
        lock (_sync)
        {
            while (notifiers.Any(n => _leases.ContainsKey(n)))
            {
                Monitor.Wait(_sync);
            }
        }
    }

    private sealed class Lease(NotifierManager owner, INotifier notifier)
    {
        public INotifier Notifier { get; } = notifier;
        public NotifierManager Owner { get; } = owner;
    }
}
