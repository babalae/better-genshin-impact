using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.Service.Notification
{
    public interface INotifier
    {
        bool IsEnabled { get; }

        Task<NotificationResponse> Notify(TaskNotificationData notificationData);

        Task<NotificationResponse> Notify(LifecycleNotificationData notificationData);
    }

    public class WebhookNotifier : INotifier
    {
        private readonly HttpClient _httpClient;

        public bool IsEnabled { get; set; }
        public string Endpoint { get; set; } = "";

        public WebhookNotifier()
        {
            _httpClient = new HttpClient();
        }

        private async Task<NotificationResponse> SendNotification(StringContent content)
        {
            try
            {
                var response = await _httpClient.PostAsync(Endpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    return NotificationResponse.Error($"Webhook call failed with code: {response.StatusCode}");
                }
                return NotificationResponse.Success();
            }
            catch (Exception ex)
            {
                return NotificationResponse.Error($"Error sending webhook: {ex.Message}");
            }
        }

        public async Task<NotificationResponse> Notify(TaskNotificationData notificationData)
        {
            var serializedData = JsonConvert.SerializeObject(notificationData);

            var content = new StringContent(serializedData, Encoding.UTF8, "application/json");

            return await SendNotification(content);
        }

        public async Task<NotificationResponse> Notify(LifecycleNotificationData notificationData)
        {
            var serializedData = JsonConvert.SerializeObject(notificationData);

            var content = new StringContent(serializedData, Encoding.UTF8, "application/json");

            return await SendNotification(content);
        }
    }

    public class NotificationResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = "";

        public static NotificationResponse Success()
        {
            return new NotificationResponse { IsSuccess = true, Message = "Success" };
        }

        public static NotificationResponse Error(string message)
        {
            return new NotificationResponse { IsSuccess = false, Message = message };
        }
    }

    public enum NotificationEvent
    {
        GeniusInvocation,
        Domain
    }

    public enum NotificationAction
    {
        Started,
        Completed,
        Progress
    }

    public enum NotificationConclusion
    {
        Success,
        Failure,
        Cancelled
    }

    [Serializable]
    public class TaskNotificationData
    {
        [JsonProperty("event")]
        // convert to camelCase
        [JsonConverter(typeof(StringEnumConverter), true)]
        public NotificationEvent Event { get; set; }

        [JsonProperty("action")]
        [JsonConverter(typeof(StringEnumConverter), true)]
        public NotificationAction Action { get; set; }

        [JsonProperty("conclusion")]
        [JsonConverter(typeof(StringEnumConverter), true)]
        public NotificationConclusion? Conclusion { get; set; }

        [JsonProperty("task")]
        public object? Task { get; set; }
    }

    [Serializable]
    public class LifecycleNotificationData
    {
        [JsonProperty("payload")]
        public string Payload { get; set; }

        public static LifecycleNotificationData Test()
        {
            return new LifecycleNotificationData { Payload = "test" };
        }
    }

    public class NotificationManager
    {
        private static NotificationManager? _instance;
        private AllConfig Config { get; set; }

        private readonly List<INotifier> _notifiers = [];

        public NotificationManager(IConfigService configService)
        {
            Config = configService.Get();
            _instance = this;
            RegisterNotifier(new WebhookNotifier
            {
                Endpoint = Config.NotificationConfig.WebhookEndpoint,
                IsEnabled = Config.NotificationConfig.WebhookEnabled
            });
        }

        public static NotificationManager Instance()
        {
            if (_instance == null)
            {
                throw new Exception("Not instantiated");
            }
            return _instance;
        }

        public void RefreshNotifiers()
        {
            foreach (var notifier in _notifiers)
            {
                switch (notifier)
                {
                    case WebhookNotifier webhookNotifier:
                        webhookNotifier.IsEnabled = Config.NotificationConfig.WebhookEnabled;
                        webhookNotifier.Endpoint = Config.NotificationConfig.WebhookEndpoint;
                        break;

                    default:
                        throw new Exception("Unknown notifier");
                }
            }
        }

        public void RegisterNotifier(INotifier observer)
        {
            _notifiers.Add(observer);
        }

        public INotifier GetNotifier<T>() where T : INotifier
        {
            return _notifiers.FirstOrDefault(o => o is T);
        }

        public void NotifyObservers(TaskNotificationData d)
        {
            foreach (var observer in _notifiers)
            {
                observer.Notify(d);
            }
        }
    }
}
