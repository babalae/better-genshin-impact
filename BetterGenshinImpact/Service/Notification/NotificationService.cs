using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Notification;

/// <summary>
///     通知服务类，管理和分发各种通知
/// </summary>
public class NotificationService : IHostedService, IDisposable
{
    private static readonly object InstanceLock = new();
    private static NotificationService? _instance;

    private readonly NotifierManager _notifierManager;
    private readonly HttpClient _notifyHttpClient;
    private readonly CancellationTokenSource? _webSocketCts;

    // 缓存配置对象引用
    private NotificationConfig? _notificationConfig;

    /// <summary>
    ///     构造函数
    /// </summary>
    public NotificationService(NotifierManager notifierManager)
    {
        _notifierManager = notifierManager ?? throw new ArgumentNullException(nameof(notifierManager));
        _notifyHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _webSocketCts = new CancellationTokenSource();

        lock (InstanceLock)
        {
            _instance = this;
        }
    }

    /// <summary>
    ///     释放资源
    /// </summary>
    public void Dispose()
    {
        // _webSocketCts?.Cancel();
        _webSocketCts?.Dispose();
        _notifyHttpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     服务启动时初始化所有通知器
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _notificationConfig = TaskContext.Instance().Config.NotificationConfig;
        InitializeNotifiers();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     服务停止时取消WebSocket连接
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _webSocketCts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     获取NotificationService单例实例
    /// </summary>
    public static NotificationService Instance()
    {
        if (_instance == null) throw new InvalidOperationException("NotificationService 未初始化");

        return _instance;
    }

    /// <summary>
    ///     初始化所有通知器
    ///     注意：添加新的通知渠道时，需在此处添加相应的初始化方法调用
    /// </summary>
    private void InitializeNotifiers()
    {
        if (_notificationConfig == null) _notificationConfig = TaskContext.Instance().Config.NotificationConfig;

        // 初始化各类通知器
        InitializeWebhookNotifier();
        InitializeWindowsUwpNotifier();
        InitializeFeishuNotifier();
        InitializeOneBotNotifier();
        InitializeWorkWeixinNotifier();
        InitializeWebSocketNotifier();
        InitializeBarkNotifier();
        InitializeEmailNotifier();
        InitializeDingDingNotifier();
        InitializeTelegramNotifier();
        InitializeXxtuiNotifier();
        InitializeDiscordWebhookNotifier();
        InitializeServerChanNotifier();

        // 添加新通知渠道时，在此处添加对应的初始化方法调用
    }

    // =================== 各类通知器初始化方法 ===================
    // 添加新的通知渠道时，参考以下模板创建新的初始化方法

    /// <summary>
    ///     初始化Webhook通知器
    /// </summary>
    private void InitializeWebhookNotifier()
    {
        if (_notificationConfig?.WebhookEnabled == true)
            _notifierManager.RegisterNotifier(new WebhookNotifier(_notifyHttpClient, _notificationConfig));
    }

    /// <summary>
    ///     初始化Windows UWP通知器
    /// </summary>
    private void InitializeWindowsUwpNotifier()
    {
        if (_notificationConfig?.WindowsUwpNotificationEnabled == true)
        {
            _notifierManager.RegisterNotifier(new WindowsUwpNotifier());
        }
    }

    /// <summary>
    ///     初始化飞书通知器
    /// </summary>
    private void InitializeFeishuNotifier()
    {
        if (_notificationConfig?.FeishuNotificationEnabled == true)
            _notifierManager.RegisterNotifier(new FeishuNotifier(
                _notifyHttpClient,
                _notificationConfig.FeishuWebhookUrl,
                _notificationConfig.FeishuAppId,
                _notificationConfig.FeishuAppSecret
            ));
    }

    /// <summary>
    ///     初始化OneBot通知器
    /// </summary>
    private void InitializeOneBotNotifier()
    {
        if (_notificationConfig?.OneBotNotificationEnabled == true)
            _notifierManager.RegisterNotifier(new OneBotNotifier(
                _notifyHttpClient,
                _notificationConfig.OneBotEndpoint,
                _notificationConfig.OneBotUserId,
                _notificationConfig.OneBotGroupId,
                _notificationConfig.OneBotToken
            ));
    }

    /// <summary>
    ///     初始化企业微信通知器
    /// </summary>
    private void InitializeWorkWeixinNotifier()
    {
        if (_notificationConfig?.WorkweixinNotificationEnabled == true)
        {
            _notifierManager.RegisterNotifier(new WorkWeixinNotifier(
                _notifyHttpClient,
                _notificationConfig.WorkweixinWebhookUrl
            ));
        }
    }

    /// <summary>
    ///     初始化WebSocket通知器
    /// </summary>
    private void InitializeWebSocketNotifier()
    {
        if (_notificationConfig?.WebSocketNotificationEnabled == true)
        {
            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            _notifierManager.RegisterNotifier(new WebSocketNotifier(
                _notificationConfig.WebSocketEndpoint,
                jsonSerializerOptions,
                _webSocketCts ?? new CancellationTokenSource()
            ));
        }
    }

    /// <summary>
    ///     初始化Bark通知器
    /// </summary>
    private void InitializeBarkNotifier()
    {
        if (_notificationConfig?.BarkNotificationEnabled == true)
        {
            _notifierManager.RegisterNotifier(new BarkNotifier(
                _notificationConfig.BarkDeviceKeys,
                _notificationConfig.BarkApiEndpoint,
                _notificationConfig.BarkGroup,
                _notificationConfig.BarkSound,
                _notificationConfig.BarkIcon
            ));
        }
    }

    /// <summary>
    ///     初始化邮件通知器
    /// </summary>
    private void InitializeEmailNotifier()
    {
        if (_notificationConfig?.EmailNotificationEnabled == true)
        {
            _notifierManager.RegisterNotifier(new EmailNotifier(
                _notificationConfig.SmtpServer,
                _notificationConfig.SmtpPort,
                _notificationConfig.SmtpUsername,
                _notificationConfig.SmtpPassword,
                _notificationConfig.FromEmail,
                _notificationConfig.FromName,
                _notificationConfig.ToEmail
            ));
        }
    }

    /// <summary>
    ///     初始化钉钉通知器
    /// </summary>
    private void InitializeDingDingNotifier()
    {
        if (_notificationConfig?.DingDingwebhookNotificationEnabled == true &&
            !string.IsNullOrEmpty(_notificationConfig.DingdingWebhookUrl) &&
            !string.IsNullOrEmpty(_notificationConfig.DingDingSecret))
        {
            _notifierManager.RegisterNotifier(new DingDingWebhook(
                _notifyHttpClient,
                _notificationConfig.DingdingWebhookUrl,
                _notificationConfig.DingDingSecret
            ));
        }
    }

    /// <summary>
    ///     初始化Telegram通知器
    /// </summary>
    private void InitializeTelegramNotifier()
    {
        if (_notificationConfig?.TelegramNotificationEnabled == true)
        {
            _notifierManager.RegisterNotifier(new TelegramNotifier(
                null,
                _notificationConfig.TelegramBotToken,
                _notificationConfig.TelegramChatId,
                _notificationConfig.TelegramApiBaseUrl,
                _notificationConfig.TelegramProxyUrl,
                _notificationConfig.TelegramProxyEnabled
            ));
        }
    }

    /// <summary>
    ///     初始化信息推送通知器
    /// </summary>
    private void InitializeXxtuiNotifier()
    {
        if (_notificationConfig?.XxtuiNotificationEnabled != true) return;

        var channels = ParseXxtuiChannels(_notificationConfig.XxtuiChannels);

        _notifierManager.RegisterNotifier(new XxtuiNotifier(
            _notificationConfig.XxtuiApiKey,
            _notificationConfig.XxtuiFrom,
            channels
        ));
    }

    /// <summary>
    ///     初始化 Discord 通知器
    /// </summary>
    private void InitializeDiscordWebhookNotifier()
    {
        if (_notificationConfig?.DiscordWebhookNotificationEnabled != true) return;

        _notifierManager.RegisterNotifier(new DiscordWebhookNotifier(
            _notifyHttpClient,
            _notificationConfig.DiscordWebhookUrl,
            _notificationConfig.DiscordWebhookUsername,
            _notificationConfig.DiscordWebhookAvatarUrl,
            _notificationConfig.DiscordWebhookImageEncoder
        ));
    }

    /// <summary>
    /// 初始化 ServerChan 通知器
    /// </summary>
    private void InitializeServerChanNotifier()
    {
        if (_notificationConfig?.ServerChanNotificationEnabled != true) return;

        _notifierManager.RegisterNotifier(new ServerChanNotifier(
            _notifyHttpClient,
            _notificationConfig.ServerChanSendKey
        ));
    }

    /// <summary>
    ///     解析信息推送通知渠道配置
    /// </summary>
    private static XxtuiChannel[] ParseXxtuiChannels(string channelsStr)
    {
        if (string.IsNullOrEmpty(channelsStr)) return new[] { XxtuiChannel.WX_MP };

        var channelStrings = channelsStr.Split(',');
        var validChannels = new XxtuiChannel[channelStrings.Length];
        var validCount = 0;

        foreach (var channel in channelStrings)
            if (Enum.TryParse<XxtuiChannel>(channel.Trim(), out var parsedChannel))
                validChannels[validCount++] = parsedChannel;

        if (validCount < channelStrings.Length) Array.Resize(ref validChannels, validCount);

        return validChannels.Length > 0 ? validChannels : new[] { XxtuiChannel.WX_MP };
    }

    /// <summary>
    ///     刷新所有通知器
    ///     配置更改后调用此方法应用新配置
    /// </summary>
    public void RefreshNotifiers()
    {
        _notificationConfig = TaskContext.Instance().Config.NotificationConfig;
        _notifierManager.RemoveAllNotifiers();
        InitializeNotifiers();
    }

    /// <summary>
    ///     测试指定类型的通知器
    /// </summary>
    public async Task<NotificationTestResult> TestNotifierAsync<T>() where T : INotifier
    {
        try
        {
            var notifier = _notifierManager.GetNotifier<T>();
            if (notifier == null)
            {
                return NotificationTestResult.Error("通知类型未启用");
            }

            var testData = CreateTestNotificationData();
            await notifier.SendAsync(testData);
            return NotificationTestResult.Success();
        }
        catch (NotifierException ex)
        {
            return NotificationTestResult.Error(ex.Message);
        }
        catch (Exception ex)
        {
            return NotificationTestResult.Error($"测试通知时发生未知错误: {ex.Message}");
        }
    }

    /// <summary>
    ///     创建测试用的通知数据
    /// </summary>
    private static BaseNotificationData CreateTestNotificationData()
    {
        var testData = new BaseNotificationData
        {
            Event = NotificationEvent.Test.Code,
            Result = NotificationEventResult.Success,
            Message = "这是一条测试通知信息"
        };

        if (TaskContext.Instance().IsInitialized)
        {
            try
            {
                testData.Screenshot = TaskControl.CaptureToRectArea().CacheImage;
            }
            catch (Exception ex)
            {
                TaskControl.Logger.LogWarning(ex, "获取测试通知截图失败");
            }
        }

        return testData;
    }

    /// <summary>
    ///     向所有通知器发送通知（异步方法）
    /// </summary>
    public async Task NotifyAllNotifiersAsync(BaseNotificationData notificationData)
    {
        if (notificationData == null) throw new ArgumentNullException(nameof(notificationData));

        if (!ShouldSendNotification(notificationData.Event)) return;

        try
        {
            await AddScreenshotIfNeededAsync(notificationData);
            await _notifierManager.SendNotificationToAllAsync(notificationData);
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogError(ex, "发送通知时发生错误");
        }
    }

    /// <summary>
    ///     判断是否应该发送此类型的通知
    /// </summary>
    private bool ShouldSendNotification(string eventCode)
    {
        var subscribeEventStr = _notificationConfig?.NotificationEventSubscribe;
        if (string.IsNullOrEmpty(subscribeEventStr)) return true;

        return subscribeEventStr.Contains(eventCode);
    }

    /// <summary>
    ///     如果需要，为通知添加截图
    /// </summary>
    private async Task AddScreenshotIfNeededAsync(BaseNotificationData notificationData)
    {
        if (_notificationConfig?.IncludeScreenShot != true)
        {
            return;
        }

        try
        {
            var mat = TaskControl.CaptureGameImageNoRetry(TaskTriggerDispatcher.GlobalGameCapture);
            if (mat != null)
            {
                var imageRegion = new ImageRegion(mat, 0, 0);
                notificationData.Screenshot = imageRegion.CacheImage;
            }
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogDebug(ex, "补充通知截图失败");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    ///     向所有通知器发送通知（非阻塞方法）
    /// </summary>
    public void NotifyAllNotifiers(BaseNotificationData notificationData)
    {
        if (notificationData == null) throw new ArgumentNullException(nameof(notificationData));

        Task.Run(async () =>
        {
            try
            {
                await NotifyAllNotifiersAsync(notificationData);
            }
            catch (Exception ex)
            {
                TaskControl.Logger.LogError(ex, "后台发送通知时发生错误");
            }
        });
    }
}