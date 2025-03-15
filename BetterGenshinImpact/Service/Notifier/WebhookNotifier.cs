using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using System.Net.Http;
using System.Text;
using System.Text.Json; // 确保 System.Text.Json 命名空间被引用
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using System.Collections.Generic;
using BetterGenshinImpact.Service.Notification; // 添加对 System.Collections.Generic 命名空间的引用
using BetterGenshinImpact.Service.Notification; // 添加对 NotificationConfig 类型的引用
using System.Text.Json; // 添加对 System.Text.Json 命名空间的引用

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
        var dataToSend = new Dictionary<string, object>
        {
            { "send_to", SendTo },
            { "event", notificationData.Event },
            { "result", notificationData.Result },
            { "timestamp", notificationData.Timestamp },
            { "screenshot", notificationData.Screenshot },
            { "message", notificationData.Message },
            { "data", notificationData.Data }
        };

        var serializedData = JsonSerializer.Serialize(dataToSend, _jsonSerializerOptions);

        return new StringContent(serializedData, Encoding.UTF8, "application/json");
    }
}