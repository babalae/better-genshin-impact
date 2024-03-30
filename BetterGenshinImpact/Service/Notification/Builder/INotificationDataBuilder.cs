using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notification.Builder;

public interface INotificationDataBuilder<TNotificationData> where TNotificationData : INotificationData
{
    TNotificationData Build();
}
