using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notifier;

public class NotifierManager
{
    private readonly List<INotifier> _notifiers = [];

    public static ILogger Logger { get; } = App.GetLogger<NotifierManager>();

    public NotifierManager()
    {
    }

    public void RegisterNotifier(INotifier notifier)
    {
        _notifiers.Add(notifier);
    }

    public void RemoveNotifier<T>() where T : INotifier
    {
        _notifiers.RemoveAll(o => o is T);
    }

    public void RemoveAllNotifiers()
    {
        _notifiers.Clear();
    }

    public INotifier? GetNotifier<T>() where T : INotifier
    {
        return _notifiers.FirstOrDefault(o => o is T);
    }

    public async Task SendNotificationAsync(INotifier notifier, INotificationData content)
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

    public async Task SendNotificationAsync<T>(INotificationData content) where T : INotifier
    {
        var notifier = _notifiers.FirstOrDefault(o => o is T);

        if (notifier != null)
        {
            await SendNotificationAsync(notifier, content);
        }
    }

    public async Task SendNotificationToAllAsync(INotificationData content)
    {
        await Task.WhenAll(_notifiers.Select(notifier => SendNotificationAsync(notifier, content)));
    }
}
