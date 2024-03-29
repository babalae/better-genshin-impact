using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Service.Notification.Converter;

public class ImageToBase64Converter : JsonConverter<Image>
{
    public override Image? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.GetString() is string base64)
        {
            return Image.FromStream(new MemoryStream(Convert.FromBase64String(base64)));
        }
        return default;
    }

    public override void Write(Utf8JsonWriter writer, Image value, JsonSerializerOptions options)
    {
        using var ms = new MemoryStream();
        value.Save(ms, ImageFormat.Jpeg);
        writer.WriteBase64StringValue(ms.ToArray());
    }
}
