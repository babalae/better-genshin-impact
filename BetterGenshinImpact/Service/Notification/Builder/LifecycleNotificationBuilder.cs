using System.Drawing;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;

namespace BetterGenshinImpact.Service.Notification.Builder;

public class LifecycleNotificationBuilder : INotificationDataBuilder
{
    private LifecycleNotificationData _notificationData = new();

    public LifecycleNotificationBuilder AddPayload(string payload)
    {
        _notificationData.Payload = payload;
        return this;
    }

    public INotificationData Build()
    {
        return _notificationData;
    }

    public void Send()
    {
        NotificationService.Instance().NotifyAllNotifiers(Build());
    }
}
