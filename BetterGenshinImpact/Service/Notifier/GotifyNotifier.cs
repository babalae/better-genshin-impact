using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;

namespace BetterGenshinImpact.Service.Notifier;

public sealed class GotifyNotifier : INotifier
{
    public string Name { get; } = "Gotify";

    private readonly HttpClient _httpClient;
    private readonly string _url;
    private readonly string _appToken;
    private readonly int _priority;

    public GotifyNotifier(HttpClient httpClient, string url, string appToken, int priority)
    {
        _httpClient = httpClient;
        _url = url.TrimEnd('/');
        _appToken = appToken;
        _priority = priority;
    }

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrWhiteSpace(_url))
            throw new NotifierException("Gotify 服务地址为空");

        if (string.IsNullOrWhiteSpace(_appToken))
            throw new NotifierException("Gotify App Token 为空");

        if (!Uri.TryCreate(_url, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new NotifierException("Gotify 服务地址无效");

        try
        {
            var endpoint = $"{_url}/message";
            var body = new
            {
                title = "BetterGI·更好的原神",
                message = GenerateMessage(content),
                priority = Math.Max(0, _priority)
            };

            var json = JsonSerializer.Serialize(body);
            using var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = requestContent
            };
            request.Headers.Add("X-Gotify-Key", _appToken);

            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                throw new NotifierException($"Gotify 调用失败，状态码: {response.StatusCode}");
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"Error sending Gotify message: {ex.Message}");
        }
    }

    private static string GenerateMessage(BaseNotificationData data)
    {
        var message = new StringBuilder();
        message.AppendLine($"时间: {data.Timestamp:yyyy-MM-dd HH:mm:ss}");

        if (!string.IsNullOrWhiteSpace(data.Message))
        {
            message.AppendLine();
            message.AppendLine($"消息: {data.Message}");
        }

        return message.ToString();
    }
}
