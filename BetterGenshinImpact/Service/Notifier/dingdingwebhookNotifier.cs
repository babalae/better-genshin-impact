using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Interface;

namespace BetterGenshinImpact.Service.Notifier;

public class DingDingWebhook : INotifier
{
    private readonly HttpClient _httpClient;
    private readonly string _secret;
    private readonly string _webhookUrl;

    public DingDingWebhook(HttpClient httpClient, string webhookUrl, string secret)
    {
        _httpClient = httpClient;
        _webhookUrl = webhookUrl;
        _secret = secret;
    }

    public string Name { get; set; } = "DingDing";

    public async Task SendAsync(BaseNotificationData content)
    {
        var timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
        var stringToSign = $"{timestamp}\n{_secret}";
        var encoding = Encoding.UTF8;
        using (var hmac = new HMACSHA256(encoding.GetBytes(_secret)))
        {
            var data = encoding.GetBytes(stringToSign);
            var hash = hmac.ComputeHash(data);
            var signature = Convert.ToBase64String(hash);
            var encodedSignature = HttpUtility.UrlEncode(signature);

            var url = $"{_webhookUrl}&timestamp={timestamp}&sign={encodedSignature}";

            var requestJson = new
            {
                msgtype = "text",
                text = new { content = content.Message },
                at = new { atUserIds = new List<string>(), isAtAll = false }
            };

            var json = JsonSerializer.Serialize(requestJson);
            var contentToSend = new StringContent(json, encoding, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, contentToSend);
                response.EnsureSuccessStatusCode();
            }
            catch (System.Exception ex)
            {
                throw new System.Exception("Failed to send message to DingDing", ex);
            }
        }
    }
}