using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notifier;

public class FeishuNotifier : INotifier
{
    public string Name { get; set; } = "Feishu";

    public string Endpoint { get; set; }

    private readonly HttpClient _httpClient;
    
    public FeishuNotifier(HttpClient httpClient, string endpoint = "")
    {
        _httpClient = httpClient;
        Endpoint = endpoint;
    }

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(Endpoint))
        {
            throw new NotifierException("Feishu webhook endpoint is not set");
        }
        
        try
        {
            var response = await _httpClient.PostAsync(Endpoint, TransformData(content));

            if (!response.IsSuccessStatusCode)
            {
                throw new NotifierException($"Feishu webhook call failed with code: {response.StatusCode}");
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"Error sending Feishu webhook: {ex.Message}");
        }
    }

    private StringContent TransformData(BaseNotificationData notificationData)
    {
        var feishuMessage = new
        {
            msg_type = "text",
            content = new
            {
                text = notificationData.Message
            }
        };

        var serializedData = JsonSerializer.Serialize(feishuMessage);

        return new StringContent(serializedData, Encoding.UTF8, "application/json");
    }
}