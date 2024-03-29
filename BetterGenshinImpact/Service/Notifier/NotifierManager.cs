using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notifier.Interface;

namespace BetterGenshinImpact.Service.Notifier;

public class NotifierManager
{
    private readonly List<INotifier> _notifiers = [];

    public NotifierManager()
    {
    }

    public void RegisterNotifier(INotifier notifier)
    {
        _notifiers.Add(notifier);
    }

    public void RemoveNotifier<T>() where T : INotifier
    {
    }

    public void RemoveAllNotifiers()
    {
        _notifiers.Clear();
    }

    public INotifier? GetNotifier<T>() where T : INotifier
    {
        return _notifiers.FirstOrDefault(o => o is T);
    }

    public async Task SendNotificationAsync<T>(HttpContent httpContent) where T : INotifier
    {
        var notifier = _notifiers.FirstOrDefault(o => o is T);
        if (notifier != null)
        {
            await notifier.SendNotificationAsync(httpContent);
        }
    }

    public async Task SendNotificationToAllAsync(HttpContent httpContent)
    {
        await Task.WhenAll(_notifiers.Select(notifier => notifier.SendNotificationAsync(httpContent)));
    }
}
