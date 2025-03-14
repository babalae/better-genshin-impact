using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using System.Collections.Generic; // 添加对 System.Collections.Generic 命名空间的引用

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

    public WebhookNotifier(HttpClient httpClient, string endpoint = "", string sendTo = "")
    {
        _httpClient = httpClient;
        Endpoint = endpoint;
        SendTo = sendTo; // 初始化 send_to 属性
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
        // 使用 SendTo 属性来设置 send_to 字段
        var dataToSend = new Dictionary<string, object>
        {
            { "send_to", SendTo },
            { "notification_data", notificationData }
        };

        var serializedData = JsonSerializer.Serialize(dataToSend, _jsonSerializerOptions);

        return new StringContent(serializedData, Encoding.UTF8, "application/json");
    }
}