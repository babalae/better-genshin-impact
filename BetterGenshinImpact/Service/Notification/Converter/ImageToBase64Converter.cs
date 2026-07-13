using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service.Notification.Converter;

public class ImageToBase64Converter : JsonConverter<Image<Rgb24>?>
{
    public override Image<Rgb24>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.GetString() is { Length: > 0 } base64)
        {
            return Image.Load<Rgb24>(Convert.FromBase64String(base64));
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, Image<Rgb24>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        using var ms = new MemoryStream();
        value.Save(ms, new JpegEncoder());
        writer.WriteBase64StringValue(ms.ToArray());
    }
}
