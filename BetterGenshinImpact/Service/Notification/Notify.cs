using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;

namespace BetterGenshinImpact.Service.Notification;

public class Notify
{
    public static BaseNotificationData Event(string eventName)
    {
        return new BaseNotificationData
        {
            Event = eventName
        };
    }
    
    public static BaseNotificationData Event(NotificationEvent eventEnum)
    {
        return new BaseNotificationData
        {
            Event = eventEnum.Code
        };
    }
}