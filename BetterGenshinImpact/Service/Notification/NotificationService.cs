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
///     Notification service c
/// </summary>
public class NotificationService : IHostedService, IDisposable
{
    private static readonly object InstanceLock = new();
    private static NotificationService? _instance;

    private readonly NotifierManager _notifierManager;
    private readonly HttpClient _notifyHttpClient;
    private readonly CancellationTokenSource? _webSocketCts;

    // Notification couration
    private NotificationConfig? _notificationConfig;

    /// <summary>
    ///     Constr
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
    ///     Releasces
    /// </summary>
    public void Dispose()
    {
        // _webSocketCts?.Cancel();
        _webSocketCts?.Dispose();
        _notifyHttpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Initialize notifiers wstarts
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _notificationConfig = TaskContext.Instance().Config.NotificationConfig;
        InitializeNotifiers();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Cancel WebSocket connecvice stops
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _webSocketCts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Get NotificationService single
    /// </summary>
    public static NotificationService Instance()
    {
        if (_instance == null) throw new InvalidOperationException("notification.error.serviceNotalized");

        return _instance;
    }

    /// <summary>
    ///     Initialize al
    ///     Note: When adding new notifiers, add correspon
    /// </summary>
    private void InitializeNotifiers()
    {
        if (_notificationConfig == null) _notificationConfig = TaskContext.Instance().Config.NotificationConfig;

        // Initialize als
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

        // Add initialization for new notifiers hee
    }

    // =================== Notifier initialization methods ===========
    // When adding new notifiers, use these me

    /// <summary>
    ///     Initialize Webhoifier
    /// </summary>
    private void InitializeWebhookNotifier()
    {
        if (_notificationConfig?.WebhookEnabled == true)
            _notifierManager.RegisterNotifier(new WebhookNotifier(_notifyHttpClient, _notificationConfig));
    }

    /// <summary>
    ///     Initialize Windows U
    /// </summary>
    private void InitializeWindowsUwpNotifier()
    {
        if (_notificationConfig?.WindowsUwpNotificationEnabled == true)
        {
            _notifierManager.RegisterNotifier(new WindowsUwpNotifier());
        }
    }

    /// <summary>
    ///     Initialize Fetifier
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
    ///     Initialize OneBfier
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
    ///     Initialize Workr
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
    ///     Initialize WebSockfier
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
    ///     Initialize Ba
    /// </summary>
    private void InitializeBarkNotifier()
    {
        if (_notificationConfig?.BarkNotificationEnabled == true)
        {
            _notifierManager.RegisterNotifier(new BarkNotifier(
                _notificationConfig.BarkDeviceKeys,
                _notificationConfig.BarkApiEndpoint,
                _notificationConfig.BarkGroup,
                _notificationConfig.BarkSound
            ));
        }
    }

    /// <summary>
    ///     Initialize E
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
    ///     Initialize Dier
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
    ///     Initialize Telegrier
    /// </summary>
    private void InitializeTelegramNotifier()
    {
        if (_notificationConfig?.TelegramNotificationEnabled == true)
        {
            _notifierManager.RegisterNotifier(new TelegramNotifier(
                _notifyHttpClient,
                _notificationConfig.TelegramBotToken,
                _notificationConfig.TelegramChatId,
                _notificationConfig.TelegramApiBaseUrl
            ));
        }
    }

    /// <summary>
    ///     Initialize Xxtuifier
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
    ///     Parse Xxtui notificats
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
    ///     Refresh allers
    ///     Call this method after changes
    /// </summary>
    public void RefreshNotifiers()
    {
        _notificationConfig = TaskContext.Instance().Config.NotificationConfig;
        _notifierManager.RemoveAllNotifiers();
        InitializeNotifiers();
    }

    /// <summary>
    ///     Test a specific er
    /// </summary>
    public async Task<NotificationTestResult> TestNotifierAsync<T>() where T : INotifier
    {
        try
        {
            var notifier = _notifierManager.GetNotifier<T>();
            if (notifier == null)
            {
                return NotificationTestResult.Error("notification.eled");
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
            return NotificationTestResult.Error("notification.error.unexpectedTestError");
        }
    }

    /// <summary>
    ///     Create test notif
    /// </summary>
    private static BaseNotificationData CreateTestNotificationData()
    {
        var messageService = App.GetService<NotificationMessageService>();
        var testData = new BaseNotificationData
        {
            Event = NotificationEvent.Test.Code,
            Result = NotificationEventResult.Success,
            Message = messageService != null ? messageService.TestMessage : "notification.message.test"
        };

        if (TaskContext.Instance().IsInitialized)
        {
            try
            {
                testData.Screenshot = TaskControl.CaptureToRectArea().CacheImage;
            }
            catch (Exception ex)
            {
                var errorMessage = messageService != null ? messageService.ScreenshotFailedError : "notification.error.screenshotFailed";
                TaskControl.Logger.LogWarning(ex, errorMessage);
            }
        }

        return testData;
    }

    /// <summary>
    ///     Send notification to all n
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
            TaskControl.Logger.LogError(ex, "notification.errorror");
        }
    }

    /// <summary>
    ///     Determine if notific event
    /// </summary>
    private bool ShouldSendNotification(string eventCode)
    {
        var subscribeEventStr = _notificationConfig?.NotificationEventSubscribe;
        if (string.IsNullOrEmpty(subscribeEventStr)) return true;

        return subscribeEventStr.Contains(eventCode);
    }

    /// <summary>
    ///     Add screenshot toded
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
            TaskControl.Logger.LogDebug(ex, "notification.error.screenshotFailed");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Send notification to all notifd
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
                TaskControl.Logger.LogError(ex, "notification.error.baor");
            }
        });
    }
}