using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Service.Notification.Converter;

public class BaseDateTimeJsonConverter(string format) : JsonConverter<DateTime>
{

    // 反序列化方法
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            if (DateTime.TryParseExact(reader.GetString(), format, null, System.Globalization.DateTimeStyles.None, out DateTime date))
            {
                return date;
            }
        }
        return reader.GetDateTime();
    }

    // 序列化方法
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(format));
    }
}