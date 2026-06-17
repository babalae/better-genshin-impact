using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service.Notifier;

public class WebhookNotifier : INotifier
{
    public string Name { get; set; } = "Webhook";

    public string Endpoint { get; set; }

    // 添加 send_to 属性
    private string SendTo { get; set; }

    private readonly HttpClient _httpClient;

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public WebhookNotifier(HttpClient httpClient, NotificationConfig config)
    {
        _httpClient = httpClient;
        Endpoint = config.WebhookEndpoint;
        SendTo = config.WebhookSendTo; // 初始化 send_to 属性
    }

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(Endpoint))
        {
            throw new NotifierException("Webhook 地址为空");
        }

        try
        {
            var response = await _httpClient.PostAsync(Endpoint, TransformData(content));

            if (!response.IsSuccessStatusCode)
            {
                throw new NotifierException($"Webhook call failed with code: {response.StatusCode}");
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"Error sending webhook: {ex.Message}");
        }
    }

    private StringContent TransformData(BaseNotificationData notificationData)
    {
        // 使用 SendTo 属性来设置 send_to 字段，并将 notification_data 的内容合并到外层字典
        var dataToSend = new Dictionary<string, object?>
        {
            { "send_to", SendTo },
            { "event", notificationData.Event },
            { "result", notificationData.Result },
            { "timestamp", notificationData.Timestamp },
            // 修改截图数据的处理方式，先转换为字节数组再进行 Base64 编码
            { "screenshot", ConvertToBase64(notificationData.Screenshot) },
            { "message", notificationData.Message },
            { "data", notificationData.Data }
        };

        var serializedData = JsonSerializer.Serialize(dataToSend, _jsonSerializerOptions);

        return new StringContent(serializedData, Encoding.UTF8, "application/json");
    }

    // 添加新的辅助方法，用于将图像转换为 Base64 字符串
    private static string? ConvertToBase64(Image<Rgb24>? imageObj)
    {
        if (imageObj == null)
        {
            return null;
        }

        using var ms = new MemoryStream();
        imageObj.Save(ms, new JpegEncoder());
        return Convert.ToBase64String(ms.ToArray());
    }
}
