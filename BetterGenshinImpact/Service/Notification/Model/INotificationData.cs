using BetterGenshinImpact.Service.Notification.Model.Enum;
using System.Text.Json.Serialization;
using System.Drawing;
using BetterGenshinImpact.Service.Notification.Converter;

namespace BetterGenshinImpact.Service.Notification.Model;

public abstract class INotificationData
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotificationEvent Event { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotificationAction Action { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotificationConclusion? Conclusion { get; set; }

    [JsonConverter(typeof(ImageToBase64Converter))]
    public Image? Screenshot { get; set; }

    public string? Message { get; set; }

    public void Send()
    {
        NotificationHelper.Notify(this);
    }
}
