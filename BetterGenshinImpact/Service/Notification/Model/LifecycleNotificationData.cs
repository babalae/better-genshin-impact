using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Service.Notification.Model;

public class LifecycleNotificationData : INotificationData
{
    public object? Payload { get; set; }

    public static LifecycleNotificationData Test()
    {
        return new LifecycleNotificationData();
    }
}
