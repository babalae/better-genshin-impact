using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notifier;

public class WebhookNotifier : INotifier
{
    public string Name { get; set; } = "Webhook";

    public string Endpoint { get; set; }

    private readonly HttpClient _httpClient;
    
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public WebhookNotifier(HttpClient httpClient, string endpoint = "")
    {
        _httpClient = httpClient;
        Endpoint = endpoint;
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
        // using object type here so it serializes the interface correctly
        var serializedData = JsonSerializer.Serialize<object>(notificationData, _jsonSerializerOptions);

        return new StringContent(serializedData, Encoding.UTF8, "application/json");
    }
}