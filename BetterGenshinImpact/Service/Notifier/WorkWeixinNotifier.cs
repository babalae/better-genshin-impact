using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notifier;

public class WorkWeixinNotifier : INotifier
{
    public string Name { get; set; } = "WorkWeixin";

    public string Endpoint { get; set; }

    private readonly HttpClient _httpClient;
    
    public WorkWeixinNotifier(HttpClient httpClient, string endpoint = "")
    {
        _httpClient = httpClient;
        Endpoint = endpoint;
    }

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(Endpoint))
        {
            throw new NotifierException("WorkWeixin webhook endpoint is not set");
        }
        
        try
        {
            var response = await _httpClient.PostAsync(Endpoint, TransformData(content));

            if (!response.IsSuccessStatusCode)
            {
                throw new NotifierException($"WorkWeixin webhook call failed with code: {response.StatusCode}");
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"Error sending WorkWeixin webhook: {ex.Message}");
        }
    }

    private StringContent TransformData(BaseNotificationData notificationData)
    {
        var workweixinMessage = new
        {
            msgtype = "text",
            text = new
            {
                content = notificationData.Message
            }
        };

        var serializedData = JsonSerializer.Serialize(workweixinMessage);

        return new StringContent(serializedData, Encoding.UTF8, "application/json");
    }
}