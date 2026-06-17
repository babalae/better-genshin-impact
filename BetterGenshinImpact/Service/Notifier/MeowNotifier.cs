using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notifier;

public class MeowNotifier : INotifier
{
    public string Name { get; set; } = "MeoW";

    private readonly HttpClient _httpClient;
    private readonly string _nickname;
    private readonly string _title;

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public MeowNotifier(HttpClient httpClient, string nickname, string title)
    {
        _httpClient = httpClient;
        _nickname = nickname;
        _title = title;
    }

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrWhiteSpace(_nickname))
        {
            throw new NotifierException("MeoW 昵称为空");
        }

        try
        {
            var url = BuildUrl();
            using var response = await _httpClient.PostAsync(url, BuildContent(content));

            if (!response.IsSuccessStatusCode)
            {
                throw new NotifierException($"MeoW 调用失败，状态码: {response.StatusCode}");
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"Error sending MeoW message: {ex.Message}");
        }
    }

    private string BuildUrl()
    {
        var url = $"https://api.chuckfang.com/{Uri.EscapeDataString(_nickname)}";
        if (!string.IsNullOrEmpty(_title))
        {
            url += $"/{Uri.EscapeDataString(_title)}";
        }
        return url;
    }

    private StringContent BuildContent(BaseNotificationData notificationData)
    {
        var body = new
        {
            title = $"BetterGI·更好的原神",
            msg = GenerateMessage(notificationData),
        };

        var json = JsonSerializer.Serialize(body, _jsonSerializerOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static string GenerateMessage(BaseNotificationData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"时间: {data.Timestamp:yyyy-MM-dd HH:mm:ss}");

        if (!string.IsNullOrEmpty(data.Message))
        {
            sb.AppendLine();
            sb.AppendLine($"事件消息: {data.Message}");
        }

        return sb.ToString();
    }
}
