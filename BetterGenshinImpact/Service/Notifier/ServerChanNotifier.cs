using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// ServerChan通知器
/// </summary>
public class ServerChanNotifier : INotifier
{
    public string Name { get; set; } = "ServerChan";

    private readonly HttpClient _httpClient;
    private readonly string _sendKey;


    public ServerChanNotifier(HttpClient httpClient, string sendKey)
    {
        _httpClient = httpClient;
        _sendKey = sendKey;
    }

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(_sendKey))
        {
            throw new NotifierException("ServerChan SendKey为空");
        }

        try
        {
            // 获取正确的API URL
            string apiUrl = GetServerChanApiUrl(_sendKey);

            // 生成通知标题和内容
            string title = $"BetterGI·更好的原神";
            string desp = GenerateDescription(content);

            // 准备表单数据
            var postData = $"title={Uri.EscapeDataString(title)}&desp={Uri.EscapeDataString(desp)}";

            // 创建请求
            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");

            // 发送请求
            var response = await _httpClient.SendAsync(request);

            // 检查响应状态
            if (!response.IsSuccessStatusCode)
            {
                throw new NotifierException($"ServerChan调用失败，状态码: {response.StatusCode}");
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"Error sending ServerChan message: {ex.Message}");
        }
    }

    /// <summary>
    /// 根据sendKey格式获取正确的API URL
    /// </summary>
    private string GetServerChanApiUrl(string key)
    {
        // 判断sendkey是否以"sctp"开头并提取数字部分
        if (key.StartsWith("sctp"))
        {
            var match = Regex.Match(key, @"^sctp(\d+)t");
            if (match.Success)
            {
                var num = match.Groups[1].Value;
                return $"https://{num}.push.ft07.com/send/{key}.send";
            }
            else
            {
                throw new ArgumentException("Invalid key format for sctp.");
            }
        }
        else
        {
            return $"https://sctapi.ftqq.com/{key}.send";
        }
    }

    /// <summary>
    /// 生成通知描述内容
    /// </summary>
    private string GenerateDescription(BaseNotificationData data)
    {
        var sb = new StringBuilder();

        // 添加事件时间
        sb.AppendLine($"**时间**: {data.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")}");

        // 添加事件消息
        if (!string.IsNullOrEmpty(data.Message))
        {
            sb.AppendLine();
            sb.AppendLine($"**消息**: {data.Message}");
        }

        return sb.ToString();
    }
}