using BetterGenshinImpact.Service.Notification.Model.Enum;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Service.Notification.Model;

public interface INotificationData
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotificationEvent Event { get; set; }
}
