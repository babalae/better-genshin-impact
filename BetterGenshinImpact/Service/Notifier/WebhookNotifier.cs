using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using BetterGenshinImpact.Service.Interface;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using System.Collections.Generic;
using BetterGenshinImpact.Service.Notification;

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
            var localizationService = App.GetService<ILocalizationService>();
            var errorMessage = localizationService != null ? localizationService.GetString("notification.error.webhookEmpty") : "Webhook 地址为空";
            throw new NotifierException(errorMessage);
        }
        
        try
        {
            var response = await _httpClient.PostAsync(Endpoint, TransformData(content));

            if (!response.IsSuccessStatusCode)
            {
                var localizationService = App.GetService<ILocalizationService>();
                var errorMessage = localizationService != null ? localizationService.GetString("notification.error.webhookFailed", response.StatusCode) : $"Webhook call failed with code: {response.StatusCode}";
                throw new NotifierException(errorMessage);
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            var localizationService = App.GetService<ILocalizationService>();
            var errorMessage = localizationService != null ? localizationService.GetString("notification.error.webhookError", ex.Message) : $"Error sending webhook: {ex.Message}";
            throw new NotifierException(errorMessage);
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