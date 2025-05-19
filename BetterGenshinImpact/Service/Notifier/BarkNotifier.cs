using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;

namespace BetterGenshinImpact.Service.Notifier
{
    /// <summary>
    /// Bark通知配置选项
    /// </summary>
    public class BarkOptions
    {
        /// <summary>
        /// 推送标题
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 推送副标题
        /// </summary>
        public string Subtitle { get; set; }

        /// <summary>
        /// 推送内容
        /// </summary>
        public string Body { get; set; }

        /// <summary>
        /// 设备Key
        /// </summary>
        public string DeviceKey { get; set; }

        /// <summary>
        /// 推送中断级别：critical(重要警告), active(默认值), timeSensitive(时效性通知), passive(仅添加到通知列表)
        /// </summary>
        public string Level { get; set; } = "active";

        /// <summary>
        /// 通知声音，填1时铃声重复播放
        /// </summary>
        public string Sound { get; set; } = "bell";

        /// <summary>
        /// 重要警告的通知音量，取值范围: 0-10, 不传默认值为5
        /// </summary>
        public int? Volume { get; set; }

        /// <summary>
        /// 推送角标，可以是任意数字
        /// </summary>
        public int? Badge { get; set; }

        /// <summary>
        /// 通知铃声重复播放，传1开启
        /// </summary>
        public string Call { get; set; }

        /// <summary>
        /// iOS14.5以下自动复制推送内容，传1开启
        /// </summary>
        public string AutoCopy { get; set; }

        /// <summary>
        /// 复制推送时指定复制的内容
        /// </summary>
        public string Copy { get; set; }

        /// <summary>
        /// 为推送设置自定义图标，设置的图标将替换默认Bark图标
        /// </summary>
        public string Icon { get; set; }

        /// <summary>
        /// 对消息进行分组，推送将按group分组显示在通知中心中
        /// </summary>
        public string Group { get; set; } = "default";

        /// <summary>
        /// 加密推送的密文
        /// </summary>
        public string Ciphertext { get; set; }

        /// <summary>
        /// 传1保存推送，传其他的不保存推送
        /// </summary>
        public string IsArchive { get; set; }

        /// <summary>
        /// 点击推送时跳转的URL
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// 传"none"时，点击推送不会弹窗
        /// </summary>
        public string Action { get; set; }
    }

    public class BarkNotifier : INotifier
    {
        public string Name { get; set; } = "Bark";

        // Bark API配置
        private readonly string _apiBaseUrl;
        private readonly string[] _deviceKeys;
        private readonly string[] _group;
        private readonly string[] _sound;
        private readonly HttpClient _httpClient;
        private readonly BarkOptions _defaultOptions;

        /// <summary>
        /// Bark通知器构造函数
        /// </summary>
        /// <param name="deviceKeys">设备密钥，多个设备使用逗号、分号或空格分隔</param>
        /// <param name="apiHost">Bark API基础URL</param>
        /// <param name="options">Bark通知默认选项</param>
        public BarkNotifier(
            string deviceKeys,
            string apiHost,
            string group,
            string sound,
            BarkOptions options = null)
        {
            // 输入验证
            if (string.IsNullOrEmpty(deviceKeys))
                throw new ArgumentException("必须提供至少一个设备密钥", nameof(deviceKeys));

            // 确保主机名格式正确
            apiHost = apiHost.TrimEnd('/');
            if (!apiHost.StartsWith("http://") && !apiHost.StartsWith("https://"))
                apiHost = "https://" + apiHost;
                
            _apiBaseUrl = apiHost;
            
            // 将逗号分隔的设备密钥字符串转换为数组
            _deviceKeys = deviceKeys.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (_deviceKeys.Length == 0)
                throw new ArgumentException("必须提供至少一个有效的设备密钥", nameof(deviceKeys));
            
            // 初始化默认选项
            _defaultOptions = options ?? new BarkOptions();
            _defaultOptions.Group = group;
            _defaultOptions.Sound = sound;

            // 使用HttpClient进行API调用
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10); // 设置合理的超时时间
        }

        /// <summary>
        /// 异步发送通知（使用默认选项）
        /// </summary>
        /// <param name="content">通知内容</param>
        public async Task SendAsync(BaseNotificationData content)
        {
            await SendAsync(content, null);
        }

        /// <summary>
        /// 异步发送通知（自定义选项）
        /// </summary>
        /// <param name="content">通知内容</param>
        /// <param name="options">自定义Bark选项</param>
        public async Task SendAsync(BaseNotificationData content, BarkOptions options)
        {
            try
            {
                // 合并默认选项和自定义选项
                var mergedOptions = MergeOptions(_defaultOptions, options);
                
                // 格式化通知数据
                var payload = new Dictionary<string, object>
                {
                    ["title"] = mergedOptions.Title ?? FormatNotificationTitle(content),
                    ["body"] = mergedOptions.Body ?? FormatNotificationBody(content)
                };

                // 添加其他可选参数
                if (!string.IsNullOrEmpty(mergedOptions.Subtitle))
                    payload["subtitle"] = mergedOptions.Subtitle;
                
                if (!string.IsNullOrEmpty(mergedOptions.Level))
                    payload["level"] = mergedOptions.Level;
                
                if (!string.IsNullOrEmpty(mergedOptions.Sound))
                    payload["sound"] = mergedOptions.Sound;
                
                if (mergedOptions.Volume.HasValue)
                    payload["volume"] = mergedOptions.Volume.Value;
                
                if (mergedOptions.Badge.HasValue)
                    payload["badge"] = mergedOptions.Badge.Value;
                
                if (!string.IsNullOrEmpty(mergedOptions.Call))
                    payload["call"] = mergedOptions.Call;
                
                if (!string.IsNullOrEmpty(mergedOptions.AutoCopy))
                    payload["autoCopy"] = mergedOptions.AutoCopy;
                
                if (!string.IsNullOrEmpty(mergedOptions.Copy))
                    payload["copy"] = mergedOptions.Copy;
                
                if (!string.IsNullOrEmpty(mergedOptions.Icon))
                    payload["icon"] = mergedOptions.Icon;
                
                if (!string.IsNullOrEmpty(mergedOptions.Group))
                    payload["group"] = mergedOptions.Group;
                
                if (!string.IsNullOrEmpty(mergedOptions.Ciphertext))
                    payload["ciphertext"] = mergedOptions.Ciphertext;
                
                if (!string.IsNullOrEmpty(mergedOptions.IsArchive))
                    payload["isArchive"] = mergedOptions.IsArchive;
                
                if (!string.IsNullOrEmpty(mergedOptions.Url))
                    payload["url"] = mergedOptions.Url;
                
                if (!string.IsNullOrEmpty(mergedOptions.Action))
                    payload["action"] = mergedOptions.Action;

                // 序列化通知数据
                var jsonPayload = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // 为每个设备发送单独的请求
                var tasks = new List<Task>();
                foreach (var deviceKey in _deviceKeys)
                {
                    tasks.Add(SendToDeviceAsync(deviceKey, httpContent, jsonPayload));
                }
                
                // 等待所有请求完成
                await Task.WhenAll(tasks);
                
                // 检查任务是否有异常
                foreach (var task in tasks)
                {
                    if (task.Exception != null)
                    {
                        throw task.Exception;
                    }
                }
            }
            catch (System.Exception ex)
            {
                throw new NotifierException($"Bark通知发送失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 向单个设备发送请求
        /// </summary>
        private async Task SendToDeviceAsync(string deviceKey, HttpContent httpContent, string jsonPayload)
        {
            try
            {
                // 构建URL（按照API要求，设备Key放在URL路径中）
                var requestUrl = $"{_apiBaseUrl}/{deviceKey}";
                
                // 输出请求信息用于调试
                Console.WriteLine($"发送通知到: {requestUrl}");
                Console.WriteLine($"请求内容: {jsonPayload}");
                
                // 发送到API端点
                var response = await _httpClient.PostAsync(requestUrl, httpContent);
                
                // 读取响应内容
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"服务器返回错误: {(int)response.StatusCode} {response.StatusCode}");
                    Console.Error.WriteLine($"响应内容: {responseContent}");
                    throw new HttpRequestException($"服务器返回错误: {(int)response.StatusCode} {response.StatusCode}, 响应内容: {responseContent}");
                }
                
                Console.WriteLine($"通知发送成功: {responseContent}");
            }
            catch (HttpRequestException ex)
            {
                // 记录发送失败
                Console.Error.WriteLine($"设备 {deviceKey} 通知发送失败: {ex.Message}");
                throw new NotifierException($"Bark通知发送失败 (设备: {deviceKey}): {ex.Message}");
            }
        }

        /// <summary>
        /// 合并默认选项和自定义选项
        /// </summary>
        private BarkOptions MergeOptions(BarkOptions defaultOptions, BarkOptions customOptions)
        {
            if (customOptions == null)
                return defaultOptions;

            Console.Error.WriteLine($"customOptions {customOptions}");
            return new BarkOptions
            {
                Title = customOptions.Title ?? defaultOptions.Title,
                Subtitle = customOptions.Subtitle ?? defaultOptions.Subtitle,
                Body = customOptions.Body ?? defaultOptions.Body,
                DeviceKey = customOptions.DeviceKey ?? defaultOptions.DeviceKey,
                Level = customOptions.Level ?? defaultOptions.Level,
                Sound = customOptions.Sound ?? defaultOptions.Sound,
                Volume = customOptions.Volume ?? defaultOptions.Volume,
                Badge = customOptions.Badge ?? defaultOptions.Badge,
                Call = customOptions.Call ?? defaultOptions.Call,
                AutoCopy = customOptions.AutoCopy ?? defaultOptions.AutoCopy,
                Copy = customOptions.Copy ?? defaultOptions.Copy,
                Icon = customOptions.Icon ?? defaultOptions.Icon,
                Group = customOptions.Group ?? defaultOptions.Group,
                Ciphertext = customOptions.Ciphertext ?? defaultOptions.Ciphertext,
                IsArchive = customOptions.IsArchive ?? defaultOptions.IsArchive,
                Url = customOptions.Url ?? defaultOptions.Url,
                Action = customOptions.Action ?? defaultOptions.Action
            };
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