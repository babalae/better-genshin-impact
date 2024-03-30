using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Logging;

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

    public async Task SendNotificationAsync(INotifier notifier, HttpContent httpContent)
    {
        try
        {
            await notifier.SendNotificationAsync(httpContent);
        }
        catch (System.Exception ex)
        {
            Logger.LogError("{name} 通知发送失败", notifier.Name);
            Debug.WriteLine(ex);
        }
    }

    public async Task SendNotificationAsync<T>(HttpContent httpContent) where T : INotifier
    {
        var notifier = _notifiers.FirstOrDefault(o => o is T);

        if (notifier != null)
        {
            await SendNotificationAsync(notifier, httpContent);
        }
    }

    public async Task SendNotificationToAllAsync(HttpContent httpContent)
    {
        await Task.WhenAll(_notifiers.Select(notifier => SendNotificationAsync(notifier, httpContent)));
    }
}
