using BetterGenshinImpact.Service.Notifier.Interface;
using BetterGenshinImpact.Service.Notification;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Interface;

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

    public async Task SendNotificationAsync(INotifier notifier, BaseNotificationData content)
    {
        try
        {
            await notifier.SendAsync(content);
        }
        catch (System.Exception ex)
        {
            var messageService = App.GetService<NotificationMessageService>();
            var logMessage = messageService != null ? messageService.SendFailedError : "Failed to send notification";
            Logger.LogWarning("{name} {message}: {ex}", notifier.Name, logMessage, ex.Message);
        }
    }

    public async Task SendNotificationAsync<T>(BaseNotificationData content) where T : INotifier
    {
        var notifier = _notifiers.FirstOrDefault(o => o is T);

        if (notifier != null)
        {
            await SendNotificationAsync(notifier, content);
        }
    }

    public async Task SendNotificationToAllAsync(BaseNotificationData content)
    {
        await Task.WhenAll(_notifiers.Select(notifier => SendNotificationAsync(notifier, content)));
    }
}
