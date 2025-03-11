using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq; // Added for LINQ methods
using System.Collections.Generic; // Added for potential collection operations
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
        private readonly string[] _deviceKeys;
        private readonly HttpClient _httpClient;

        // 可选的通知配置
        private readonly string _sound;
        private readonly string _group;

        /// <summary>
        /// Bark通知器构造函数
        /// </summary>
        /// <param name="apiBaseUrl">Bark API基础URL</param>
        /// <param name="deviceKeys">设备密钥数组</param>
        /// <param name="sound">可选的通知声音</param>
        /// <param name="group">可选的通知分组</param>
        public BarkNotifier(
            string[] deviceKeys,
            string apiBaseUrl = "https://api.day.app/push",
            string sound = "minuet",
            string group = "default")
        {
            // 输入验证
            if (deviceKeys == null || deviceKeys.Length == 0)
                throw new ArgumentException("必须提供至少一个设备密钥", nameof(deviceKeys));

            _apiBaseUrl = apiBaseUrl.TrimEnd('/');
            _deviceKeys = deviceKeys;
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
                    device_keys = _deviceKeys
                };

                // 序列化通知数据
                var jsonPayload = JsonSerializer.Serialize(notificationPayload);
                var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // 创建任务列表
                var tasks = new List<Task>();

                // 为每个设备创建任务
                foreach (var deviceKey in _deviceKeys)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var response = await _httpClient.PostAsync($"{_apiBaseUrl}/{deviceKey}", httpContent);
                            response.EnsureSuccessStatusCode();
                        }
                        catch (HttpRequestException ex)
                        {
                            // 记录单个设备发送失败，但不阻止其他设备通知
                            Console.Error.WriteLine($"设备 {deviceKey} 通知发送失败: {ex.Message}");
                        }
                    }));
                }

                // 等待所有任务完成
                await Task.WhenAll(tasks);
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