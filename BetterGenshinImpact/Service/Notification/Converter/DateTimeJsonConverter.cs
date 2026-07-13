using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Service.Notification.Converter;

public class DateTimeJsonConverter() : BaseDateTimeJsonConverter("yyyy-MM-dd HH:mm:ss")
{
}