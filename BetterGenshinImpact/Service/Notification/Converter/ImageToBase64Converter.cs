using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Service.Notification.Converter;

public class ImageToBase64Converter : JsonConverter<Image>
{
    public override Image Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new Bitmap(0, 0);
    }

    public override void Write(Utf8JsonWriter writer, Image value, JsonSerializerOptions options)
    {
        var stream = new MemoryStream();
        value.Save(stream, ImageFormat.Jpeg);
        byte[] byteImage = stream.ToArray();
        writer.WriteBase64StringValue(byteImage);
    }
}
