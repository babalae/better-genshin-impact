# Notification Localization Implementation Guide

This document outlines the changes needed to localize all notification messages sent through various channels (Windows, WebSocket, Webhook, Feishu, OneBot) in the BetterGenshinImpact application.

## 1. Update Language Files

### Add New Keys to Notification Message Files

Add the following keys to both `notification-messages-en.json` and `notification-messages-zh.json`:

```json
{
  "notification.channel.windows": "Windows Notification",
  "notification.channel.webhook": "Webhook",
  "notification.channel.websocket": "WebSocket",
  "notification.channel.feishu": "Feishu",
  "notification.channel.onebot": "OneBot",
  "notification.channel.telegram": "Telegram",
  "notification.channel.email": "Email",
  "notification.channel.bark": "Bark",
  "notification.channel.xxtui": "Xxtui",
  "notification.channel.dingding": "DingDing",
  "notification.channel.workweixin": "Work WeChat",
  
  "notification.error.webhookEmpty": "Webhook URL is empty",
  "notification.error.webhookFailed": "Webhook call failed with code: {0}",
  "notification.error.webhookError": "Error sending webhook: {0}",
  
  "notification.error.feishuEndpointEmpty": "Feishu webhook endpoint is not set",
  "notification.error.feishuWebhookFailed": "Feishu webhook call failed with code: {0}",
  "notification.error.feishuWebhookError": "Error sending Feishu webhook: {0}",
  "notification.error.feishuTokenFailed": "Feishu access token call failed with code: {0}",
  "notification.error.feishuTokenNotFound": "Feishu access token not found",
  "notification.error.feishuUploadFailed": "Feishu upload image failed with code: {0}",
  "notification.error.feishuImageKeyNotFound": "Feishu upload image not found image key",
  
  "notification.error.oneBotEndpointEmpty": "OneBot endpoint is not set",
  "notification.error.oneBotIdRequired": "OneBot requires either a user ID or group ID",
  "notification.error.oneBotSendFailed": "OneBot message sending failed",
  "notification.error.oneBotError": "Error sending OneBot message: {0}",
  
  "notification.error.telegramEndpointEmpty": "Telegram bot token or chat ID is not set",
  "notification.error.telegramSendFailed": "Telegram message sending failed with code: {0}",
  "notification.error.telegramError": "Error sending Telegram message: {0}",
  
  "notification.error.emailConfigIncomplete": "Email configuration is incomplete",
  "notification.error.emailSendFailed": "Failed to send email",
  "notification.error.emailError": "Error sending email: {0}",
  
  "notification.error.barkEndpointEmpty": "Bark endpoint is not set",
  "notification.error.barkSendFailed": "Bark notification failed with code: {0}",
  "notification.error.barkError": "Error sending Bark notification: {0}",
  
  "notification.error.xxtuiKeyEmpty": "Xxtui API key is not set",
  "notification.error.xxtuiSendFailed": "Xxtui notification failed with code: {0}",
  "notification.error.xxtuiError": "Error sending Xxtui notification: {0}",
  
  "notification.error.dingdingEndpointEmpty": "DingDing webhook URL is not set",
  "notification.error.dingdingSendFailed": "DingDing notification failed with code: {0}",
  "notification.error.dingdingError": "Error sending DingDing notification: {0}",
  
  "notification.error.workweixinEndpointEmpty": "Work WeChat webhook URL is not set",
  "notification.error.workweixinSendFailed": "Work WeChat notification failed with code: {0}",
  "notification.error.workweixinError": "Error sending Work WeChat notification: {0}",
  
  "notification.error.websocketConnectionFailed": "WebSocket connection failed: {0}",
  "notification.error.websocketSendFailed": "WebSocket send failed: {0}"
}
```

For the Chinese version (`notification-messages-zh.json`), use these translations:

```json
{
  "notification.channel.windows": "Windows通知",
  "notification.channel.webhook": "Webhook",
  "notification.channel.websocket": "WebSocket",
  "notification.channel.feishu": "飞书",
  "notification.channel.onebot": "OneBot",
  "notification.channel.telegram": "Telegram",
  "notification.channel.email": "邮件",
  "notification.channel.bark": "Bark",
  "notification.channel.xxtui": "信息推送",
  "notification.channel.dingding": "钉钉",
  "notification.channel.workweixin": "企业微信",
  
  "notification.error.webhookEmpty": "Webhook地址为空",
  "notification.error.webhookFailed": "Webhook调用失败，状态码: {0}",
  "notification.error.webhookError": "发送Webhook时出错: {0}",
  
  "notification.error.feishuEndpointEmpty": "飞书webhook地址未设置",
  "notification.error.feishuWebhookFailed": "飞书webhook调用失败，状态码: {0}",
  "notification.error.feishuWebhookError": "发送飞书webhook时出错: {0}",
  "notification.error.feishuTokenFailed": "获取飞书访问令牌失败，状态码: {0}",
  "notification.error.feishuTokenNotFound": "未找到飞书访问令牌",
  "notification.error.feishuUploadFailed": "飞书上传图片失败，状态码: {0}",
  "notification.error.feishuImageKeyNotFound": "飞书上传图片未找到图片密钥",
  
  "notification.error.oneBotEndpointEmpty": "OneBot端点未设置",
  "notification.error.oneBotIdRequired": "OneBot需要用户ID或群组ID",
  "notification.error.oneBotSendFailed": "OneBot消息发送失败",
  "notification.error.oneBotError": "发送OneBot消息时出错: {0}",
  
  "notification.error.telegramEndpointEmpty": "Telegram机器人令牌或聊天ID未设置",
  "notification.error.telegramSendFailed": "Telegram消息发送失败，状态码: {0}",
  "notification.error.telegramError": "发送Telegram消息时出错: {0}",
  
  "notification.error.emailConfigIncomplete": "邮件配置不完整",
  "notification.error.emailSendFailed": "发送邮件失败",
  "notification.error.emailError": "发送邮件时出错: {0}",
  
  "notification.error.barkEndpointEmpty": "Bark端点未设置",
  "notification.error.barkSendFailed": "Bark通知失败，状态码: {0}",
  "notification.error.barkError": "发送Bark通知时出错: {0}",
  
  "notification.error.xxtuiKeyEmpty": "信息推送API密钥未设置",
  "notification.error.xxtuiSendFailed": "信息推送通知失败，状态码: {0}",
  "notification.error.xxtuiError": "发送信息推送通知时出错: {0}",
  
  "notification.error.dingdingEndpointEmpty": "钉钉webhook地址未设置",
  "notification.error.dingdingSendFailed": "钉钉通知失败，状态码: {0}",
  "notification.error.dingdingError": "发送钉钉通知时出错: {0}",
  
  "notification.error.workweixinEndpointEmpty": "企业微信webhook地址未设置",
  "notification.error.workweixinSendFailed": "企业微信通知失败，状态码: {0}",
  "notification.error.workweixinError": "发送企业微信通知时出错: {0}",
  
  "notification.error.websocketConnectionFailed": "WebSocket连接失败: {0}",
  "notification.error.websocketSendFailed": "WebSocket发送失败: {0}"
}
```

## 2. Create a Base Class for Localized Notifiers

Create a new file `LocalizedNotifierBase.cs` in the `BetterGenshinImpact\Service\Notifier` directory:

```csharp
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Interface;
using System;

namespace BetterGenshinImpact.Service.Notifier
{
    /// <summary>
    /// Base class for notifiers that provides localization support
    /// </summary>
    public abstract class LocalizedNotifierBase : INotifier
    {
        protected readonly ILocalizationService? _localizationService;
        
        protected LocalizedNotifierBase()
        {
            _localizationService = App.GetService<ILocalizationService>();
        }
        
        /// <summary>
        /// Gets a localized string using the localization service
        /// </summary>
        /// <param name="key">The localization key</param>
        /// <param name="args">Optional format arguments</param>
        /// <returns>The localized string or the key if localization service is not available</returns>
        protected string GetLocalizedString(string key, params object[] args)
        {
            return _localizationService != null ? _localizationService.GetString(key, args) : key;
        }
        
        /// <summary>
        /// Gets a localized error message
        /// </summary>
        /// <param name="key">The error message key</param>
        /// <param name="args">Optional format arguments</param>
        /// <returns>The localized error message or the key if localization service is not available</returns>
        protected string GetLocalizedErrorMessage(string key, params object[] args)
        {
            return GetLocalizedString(key, args);
        }
        
        /// <summary>
        /// The name of the notifier, localized if possible
        /// </summary>
        public abstract string Name { get; }
        
        /// <summary>
        /// Sends a notification
        /// </summary>
        /// <param name="data">The notification data to send</param>
        public abstract System.Threading.Tasks.Task SendAsync(BaseNotificationData data);
    }
}
```

## 3. Update Each Notifier to Use Localization

### WindowsUwpNotifier

Update the `WindowsUwpNotifier` class to inherit from `LocalizedNotifierBase` and use localized strings:

```csharp
public class WindowsUwpNotifier : LocalizedNotifierBase
{
    public override string Name => GetLocalizedString("notification.channel.windows");

    public override Task SendAsync(BaseNotificationData data)
    {
        var toastBuilder = new ToastContentBuilder();

        if (data.Screenshot != null)
        {
            string uniqueFileName = $"notification_image_{Guid.NewGuid()}.png";
            string imagePath = Path.Combine(TempManager.GetTempDirectory(), uniqueFileName);
            data.Screenshot.SaveAsPng(imagePath);
            toastBuilder.AddHeroImage(new Uri(imagePath));
        }

        if (!string.IsNullOrEmpty(data.Message))
        {
            toastBuilder.AddText(data.Message);
        }

        toastBuilder.Show(toast =>
        {
            toast.Group = data.Event.ToString();
            toast.ExpirationTime = DateTime.Now.AddHours(12);
        });
        return Task.CompletedTask;
    }
}
```

### WebhookNotifier

Update the `WebhookNotifier` class to inherit from `LocalizedNotifierBase` and use localized strings:

```csharp
public class WebhookNotifier : LocalizedNotifierBase
{
    public override string Name => GetLocalizedString("notification.channel.webhook");

    public string Endpoint { get; set; }
    private string SendTo { get; set; }
    private readonly HttpClient _httpClient;
    
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public WebhookNotifier(HttpClient httpClient, NotificationConfig config)
    {
        _httpClient = httpClient;
        Endpoint = config.WebhookEndpoint;
        SendTo = config.WebhookSendTo;
    }

    public override async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(Endpoint))
        {
            throw new NotifierException(GetLocalizedErrorMessage("notification.error.webhookEmpty"));
        }
        
        try
        {
            var response = await _httpClient.PostAsync(Endpoint, TransformData(content));

            if (!response.IsSuccessStatusCode)
            {
                throw new NotifierException(GetLocalizedErrorMessage("notification.error.webhookFailed", response.StatusCode));
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException(GetLocalizedErrorMessage("notification.error.webhookError", ex.Message));
        }
    }

    private StringContent TransformData(BaseNotificationData notificationData)
    {
        // Implementation remains the same
    }
}
```

### FeishuNotifier

Update the `FeishuNotifier` class to inherit from `LocalizedNotifierBase` and use localized strings:

```csharp
public class FeishuNotifier : LocalizedNotifierBase
{
    public override string Name => GetLocalizedString("notification.channel.feishu");

    public string Endpoint { get; set; }
    public string AppId { get; set; }
    public string AppSecret { get; set; }
    private readonly HttpClient _httpClient;
    private static readonly string _accessTokenUrl = "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal";
    private static readonly string _uploadImageUrl = "https://open.feishu.cn/open-apis/im/v1/images";
    
    public FeishuNotifier(HttpClient httpClient, string endpoint = "", string appId = "", string appSecret = "")
    {
        _httpClient = httpClient;
        Endpoint = endpoint;
        AppId = appId;
        AppSecret = appSecret;
    }

    public override async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(Endpoint))
        {
            throw new NotifierException(GetLocalizedErrorMessage("notification.error.feishuEndpointEmpty"));
        }
        
        try
        {
            var response = await _httpClient.PostAsync(Endpoint, await TransformData(content));

            if (!response.IsSuccessStatusCode)
            {
                throw new NotifierException(GetLocalizedErrorMessage("notification.error.feishuWebhookFailed", response.StatusCode));
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException(GetLocalizedErrorMessage("notification.error.feishuWebhookError", ex.Message));
        }
    }

    private async Task<StringContent> TransformData(BaseNotificationData notificationData)
    {
        // Implementation remains the same, but update error messages to use GetLocalizedErrorMessage
    }

    private async Task<string> GetAccessToken()
    {
        // Implementation remains the same, but update error messages to use GetLocalizedErrorMessage
        // For example:
        if (!accessTokenResponse.IsSuccessStatusCode)
        {
            throw new NotifierException(GetLocalizedErrorMessage("notification.error.feishuTokenFailed", accessTokenResponse.StatusCode));
        }
        
        if (string.IsNullOrEmpty(tokenString))
        {
            throw new NotifierException(GetLocalizedErrorMessage("notification.error.feishuTokenNotFound"));
        }
    }

    private async Task<String> UploadImage(Image<Rgb24> image, string accessToken)
    {
        // Implementation remains the same, but update error messages to use GetLocalizedErrorMessage
        // For example:
        if (!uploadImageResponse.IsSuccessStatusCode)
        {
            throw new NotifierException(GetLocalizedErrorMessage("notification.error.feishuUploadFailed", uploadImageResponse.StatusCode));
        }
        
        if (string.IsNullOrEmpty(imageKey))
        {
            throw new NotifierException(GetLocalizedErrorMessage("notification.error.feishuImageKeyNotFound"));
        }
    }
}
```

### OneBotNotifier

Update the `OneBotNotifier` class to inherit from `LocalizedNotifierBase` and use localized strings:

```csharp
public class OneBotNotifier : LocalizedNotifierBase
{
    public override string Name => GetLocalizedString("notification.channel.onebot");

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

    public override async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(Endpoint))
        {
            throw new NotifierException(GetLocalizedErrorMessage("notification.error.oneBotEndpointEmpty"));
        }
        
        if (string.IsNullOrEmpty(UserId) && string.IsNullOrEmpty(GroupId))
        {
            throw new NotifierException(GetLocalizedErrorMessage("notification.error.oneBotIdRequired"));
        }

        try
        {
            // Implementation remains the same
            
            if (!success)
            {
                throw new NotifierException(GetLocalizedErrorMessage("notification.error.oneBotSendFailed"));
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException(GetLocalizedErrorMessage("notification.error.oneBotError", ex.Message));
        }
    }

    private async Task<bool> SendMessage(string url, BaseNotificationData content, bool isPrivate)
    {
        // Implementation remains the same
    }
}
```

### WebSocketNotifier

Update the `WebSocketNotifier` class to inherit from `LocalizedNotifierBase` and use localized strings:

```csharp
public class WebSocketNotifier : LocalizedNotifierBase, IDisposable
{
    private ClientWebSocket _webSocket;
    private readonly string _endpoint;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly CancellationTokenSource _cts;

    public WebSocketNotifier(string endpoint, JsonSerializerOptions jsonSerializerOptions, CancellationTokenSource cts)
    {
        _endpoint = endpoint;
        _jsonSerializerOptions = jsonSerializerOptions;
        _cts = cts;
        _webSocket = new ClientWebSocket();
    }

    public override string Name => GetLocalizedString("notification.channel.websocket");

    private async Task EnsureConnectedAsync()
    {
        if (_webSocket.State == WebSocketState.Open)
            return;

        _webSocket.Dispose();
        _webSocket = new ClientWebSocket();
        
        try
        {
            Console.WriteLine("Connecting to WebSocket...");
            await _webSocket.ConnectAsync(new Uri(_endpoint), _cts.Token);
            Console.WriteLine("WebSocket connected.");
        }
        catch (SystemException ex)
        {
            Console.WriteLine(GetLocalizedErrorMessage("notification.error.websocketConnectionFailed", ex.Message));
        }
    }

    public override async Task SendAsync(BaseNotificationData notificationData)
    {
        try
        {
            await EnsureConnectedAsync();
            var json = JsonSerializer.Serialize(notificationData, _jsonSerializerOptions);
            var buffer = System.Text.Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts.Token);
            await CloseAsync();
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine(GetLocalizedErrorMessage("notification.error.websocketSendFailed", ex.Message));
            await EnsureConnectedAsync();
        }
    }

    public async Task CloseAsync()
    {
        // Implementation remains the same
    }

    public void Dispose()
    {
        // Implementation remains the same
    }

    public async Task SendNotificationAsync(BaseNotificationData notificationData)
    {
        await SendAsync(notificationData);
    }
}
```

## 4. Update Other Notifiers

Apply the same pattern to the remaining notifiers:

- TelegramNotifier
- EmailNotifier
- BarkNotifier
- XxtuiNotifier
- DingDingWebhook
- WorkWeixinNotifier

## 5. Update NotificationService

Update the `NotificationService` class to use localized error messages:

```csharp
public class NotificationService : IHostedService, IDisposable
{
    // Existing code...
    
    private readonly ILocalizationService? _localizationService;
    
    public NotificationService(NotifierManager notifierManager)
    {
        _notifierManager = notifierManager ?? throw new ArgumentNullException(nameof(notifierManager));
        _localizationService = App.GetService<ILocalizationService>();
    }
    
    // Update error messages to use localization
    private string GetLocalizedString(string key, params object[] args)
    {
        return _localizationService != null ? _localizationService.GetString(key, args) : key;
    }
    
    // For example:
    public static NotificationService Instance()
    {
        if (_instance == null) throw new InvalidOperationException(
            App.GetService<ILocalizationService>()?.GetString("notification.error.serviceNotInitialized") ?? 
            "notification.error.serviceNotInitialized");
        
        return _instance;
    }
    
    // Update other methods to use localized strings
}
```

## 6. Testing

After implementing these changes, test each notification channel to ensure:

1. The channel name is properly localized
2. Error messages are properly localized
3. Notifications are sent correctly with localized content

## 7. Additional Considerations

- Ensure all new notification messages added to the system follow this localization pattern
- Consider adding a centralized message template system for consistent localization of notification content
- Add unit tests to verify localization of notification messages