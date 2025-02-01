using System.Net.Http;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notifier.Interface;

public interface INotifier
{
    string Name { get; }

    Task SendAsync(INotificationData data);
}
