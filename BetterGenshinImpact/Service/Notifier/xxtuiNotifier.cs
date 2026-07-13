using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Interface;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
///     推送渠道枚举
/// </summary>
public enum XxtuiChannel
{
    /// <summary>
    ///     微信公众号
    /// </summary>
    WX_MP,

    /// <summary>
    ///     企业微信群机器人
    /// </summary>
    WX_QY_ROBOT,

    /// <summary>
    ///     钉钉群机器人
    /// </summary>
    DING_ROBOT,

    /// <summary>
    ///     Bark推送
    /// </summary>
    BARK
}

/// <summary>
///     使用xxtui.com API的推送通知实现
/// </summary>
public class XxtuiNotifier : INotifier
{
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;

    /// <summary>
    ///     初始化XxtuiNotifier实例
    /// </summary>
    /// <param name="apiKey">xxtui.com的API密钥，必填</param>
    /// <param name="from">消息来源，默认为"Better原神"</param>
    /// <param name="channels">推送渠道，默认为微信公众号</param>
    public XxtuiNotifier(
        string apiKey,
        string from = "Better原神",
        params XxtuiChannel[] channels)
    {
        _httpClient = new HttpClient();
        _apiKey = apiKey;
        _baseUrl = "https://www.xxtui.com/xxtui/" + _apiKey;
        From = from.Length > 20 ? from.Substring(0, 20) : from;
        Channels = channels.Length > 0 ? channels : new[] { XxtuiChannel.WX_MP };
    }

    /// <summary>
    ///     消息来源
    /// </summary>
    public string From { get; set; }

    /// <summary>
    ///     推送渠道
    /// </summary>
    public XxtuiChannel[] Channels { get; set; }

    /// <summary>
    ///     通知名称
    /// </summary>
    public string Name => "信息推送";

    /// <summary>
    ///     发送通知
    /// </summary>
    /// <param name="data">通知数据</param>
    /// <returns>任务</returns>
    public async Task SendAsync(BaseNotificationData data)
    {
        try
        {
            var message = data.Message ?? "";

            // 格式化消息，添加事件类型
            message = string.Format("[{0}] {1}", data.Event, message);

            // 检查消息长度限制
            if (message.Length > 2000) message = message.Substring(0, 1997) + "...";

            // 准备POST请求参数
            var parameters = new Dictionary<string, string>
            {
                ["content"] = message,
                ["from"] = From
            };

            // 添加渠道参数
            if (Channels.Length > 0)
            {
                // 使用LINQ简化渠道列表生成，避免可能的字符串插值问题
                var channels = string.Join(",", Channels.Select(c => c.ToString()));
                parameters["channel"] = channels;
            }

            // 准备POST请求内容
            var content = new FormUrlEncodedContent(parameters);

            // 发送POST请求
            var response = await _httpClient.PostAsync(_baseUrl, content);

            // 确保请求成功
            response.EnsureSuccessStatusCode();

            // 记录结果（可选）
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine("Xxtui推送结果: " + responseContent);
        }
        catch (System.Exception ex)
        {
            // 记录错误
            Console.WriteLine("通过Xxtui发送通知失败: " + ex.Message);
        }
    }
}