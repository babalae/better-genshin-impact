using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using System.Collections.Generic;
using System.IO;
using System;
using SixLabors.ImageSharp;

namespace BetterGenshinImpact.Service.Notifier;

public class OneBotNotifier : INotifier
{
    public string Name { get; set; } = "OneBot";

    public string Endpoint { get; set; }
    
    public string UserId { get; set; }
    
    public string GroupId { get; set; }
    
    public string Token { get; set; }

    private readonly HttpClient _httpClient;
    
    public OneBotNotifier(HttpClient httpClient, string endpoint = "", string userId = "", string groupId = "", string token = "")
    {
        _httpClient = httpClient;
        Endpoint = endpoint;
        UserId = userId;
        GroupId = groupId;
        Token = token;
    }

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(Endpoint))
        {
            throw new NotifierException("OneBot endpoint is not set");
        }
        
        if (string.IsNullOrEmpty(UserId) && string.IsNullOrEmpty(GroupId))
        {
            throw new NotifierException("OneBot requires either a user ID or group ID");
        }

        try
        {
            // 确保URL以/send_msg结尾
            var url = Endpoint.TrimEnd('/');
            if (!url.EndsWith("/send_msg"))
            {
                url += "/send_msg";
            }

            bool success = true;

            // 处理私聊消息
            if (!string.IsNullOrEmpty(UserId))
            {
                var privateResponse = await SendMessage(url, content, true);
                if (!privateResponse)
                {
                    success = false;
                }
            }

            // 处理群聊消息
            if (!string.IsNullOrEmpty(GroupId))
            {
                var groupResponse = await SendMessage(url, content, false);
                if (!groupResponse)
                {
                    success = false;
                }
            }

            if (!success)
            {
                throw new NotifierException("OneBot message sending failed");
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"Error sending OneBot message: {ex.Message}");
        }
    }

    private async Task<bool> SendMessage(string url, BaseNotificationData content, bool isPrivate)
    {
        // 构建消息内容
        var messageContent = new List<object>
        {
            new
            {
                type = "text",
                data = new { text = content.Message }
            }
        };

        // 如果有截图，添加图片消息
        if (content.Screenshot != null)
        {
            using var ms = new MemoryStream();
            await content.Screenshot.SaveAsPngAsync(ms);
            var base64Image = Convert.ToBase64String(ms.ToArray());

            messageContent.Add(new
            {
                type = "image",
                data = new { file = $"base64://{base64Image}" }
            });
        }

        // 创建请求体
        var requestData = new Dictionary<string, object>
        {
            { "message", messageContent },
            { "message_type", isPrivate ? "private" : "group" }
        };

        if (isPrivate)
        {
            requestData.Add("user_id", UserId);
        }
        else
        {
            requestData.Add("group_id", GroupId);
        }

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestData), 
            Encoding.UTF8, 
            "application/json"
        );

        // 添加请求头
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = jsonContent;

        // 如果设置了Token，添加到请求头
        if (!string.IsNullOrEmpty(Token))
        {
            request.Headers.Add("Authorization", $"Bearer {Token}");
        }

        // 发送请求
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(responseContent);
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("status", out JsonElement statusElement))
        {
            return statusElement.GetString() == "ok";
        }
        return false;
    }
}
