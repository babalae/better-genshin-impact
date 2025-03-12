using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;

namespace BetterGenshinImpact.Service.Notifier
{
    public class BarkNotifier : INotifier
    {
        public string Name { get; set; } = "Bark";

        // Bark API配置
        private readonly string _apiBaseUrl;
        private readonly string[] _deviceKeys; // 改为设备密钥数组
        private readonly HttpClient _httpClient;

        // 可选的通知配置
        private readonly string _sound;
        private readonly string _group;

        /// <summary>
        /// Bark通知器构造函数
        /// </summary>
        /// <param name="deviceKeys">设备密钥数组</param>
        /// <param name="apiBaseUrl">Bark API基础URL</param>
        /// <param name="sound">可选的通知声音</param>
        /// <param name="group">可选的通知分组</param>
        public BarkNotifier(
            string deviceKeys,
            string apiBaseUrl = "https://api.day.app/push",
            string sound = "minuet",
            string group = "default")
        {
            // 输入验证
            if (string.IsNullOrEmpty(deviceKeys))
                throw new ArgumentException("必须提供至少一个设备密钥", nameof(deviceKeys));

            _apiBaseUrl = apiBaseUrl.TrimEnd('/');
            
            // 将逗号分隔的设备密钥字符串转换为数组
            _deviceKeys = deviceKeys.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (_deviceKeys.Length == 0)
                throw new ArgumentException("必须提供至少一个有效的设备密钥", nameof(deviceKeys));
                
            _sound = sound;
            _group = group;

            // 使用HttpClient进行API调用
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10); // 设置合理的超时时间
        }

        /// <summary>
        /// 异步发送通知
        /// </summary>
        /// <param name="content">通知内容</param>
        public async Task SendAsync(BaseNotificationData content)
        {
            try
            {
                // 格式化通知数据
                var notificationPayload = new
                {
                    title = FormatNotificationTitle(content),
                    body = FormatNotificationBody(content),
                    sound = _sound,
                    group = _group,
                    device_keys = _deviceKeys  // 使用设备密钥数组
                };

                // 序列化通知数据
                var jsonPayload = JsonSerializer.Serialize(notificationPayload);
                var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                try
                {
                    // 发送到API端点
                    var response = await _httpClient.PostAsync(_apiBaseUrl, httpContent);
                    response.EnsureSuccessStatusCode();
                }
                catch (HttpRequestException ex)
                {
                    // 记录发送失败
                    Console.Error.WriteLine($"通知发送失败: {ex.Message}");
                    throw new NotifierException($"Bark通知发送失败: {ex.Message}");
                }
            }
            catch (System.Exception ex)
            {
                throw new NotifierException($"Bark通知发送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 格式化通知标题
        /// </summary>
        private string FormatNotificationTitle(BaseNotificationData content)
        {
            return $"通知 - {content.GetType().Name}";
        }

        /// <summary>
        /// 格式化通知正文
        /// </summary>
        private string FormatNotificationBody(BaseNotificationData content)
        {
            var bodyBuilder = new StringBuilder();

            foreach (var prop in content.GetType().GetProperties())
            {
                var value = prop.GetValue(content);
                if (value != null)
                {
                    bodyBuilder.AppendLine($"{prop.Name}: {value}");
                }
            }

            return bodyBuilder.ToString();
        }
    }
}
