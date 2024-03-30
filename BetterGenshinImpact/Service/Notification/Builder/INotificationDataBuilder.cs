using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notification.Builder;

public interface INotificationDataBuilder
{
    INotificationData Build();

    void Send();
}
