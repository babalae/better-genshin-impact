using System;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Notification;

public class NotificationService : IHostedService
{
    private static NotificationService? _instance;

    private static readonly HttpClient NotifyHttpClient = new();
    private readonly NotifierManager _notifierManager;

    public NotificationService(NotifierManager notifierManager)
    {
        _notifierManager = notifierManager;
        _instance = this;
        InitializeNotifiers();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public static NotificationService Instance()
    {
        if (_instance == null) throw new Exception("Not instantiated");

        return _instance;
    }


    private void InitializeNotifiers()
    {
        if (TaskContext.Instance().Config.NotificationConfig.WebhookEnabled)
        {
            _notifierManager.RegisterNotifier(new WebhookNotifier(NotifyHttpClient,
                TaskContext.Instance().Config.NotificationConfig));
        }

        if (TaskContext.Instance().Config.NotificationConfig.WindowsUwpNotificationEnabled)
        {
            _notifierManager.RegisterNotifier(new WindowsUwpNotifier());
        }

        if (TaskContext.Instance().Config.NotificationConfig.FeishuNotificationEnabled)
        {
            _notifierManager.RegisterNotifier(new FeishuNotifier(NotifyHttpClient,
                TaskContext.Instance().Config.NotificationConfig.FeishuWebhookUrl));
        }

        if (TaskContext.Instance().Config.NotificationConfig.WorkweixinNotificationEnabled)
        {
            _notifierManager.RegisterNotifier(new WorkWeixinNotifier(NotifyHttpClient,
                TaskContext.Instance().Config.NotificationConfig.WorkweixinWebhookUrl));
        }

        // WebSocket通知初始化
        if (TaskContext.Instance().Config.NotificationConfig.WebSocketNotificationEnabled)
        {
            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
            var cts = new CancellationTokenSource();
            _notifierManager.RegisterNotifier(new WebSocketNotifier(
                TaskContext.Instance().Config.NotificationConfig.WebSocketEndpoint,
                jsonSerializerOptions,
                cts
            ));
        }

        // Bark通知初始化
        if (TaskContext.Instance().Config.NotificationConfig.BarkNotificationEnabled)
            _notifierManager.RegisterNotifier(new BarkNotifier(
                TaskContext.Instance().Config.NotificationConfig.BarkDeviceKeys,
                TaskContext.Instance().Config.NotificationConfig.BarkApiEndpoint
            ));

        // 邮件通知初始化
        if (TaskContext.Instance().Config.NotificationConfig.EmailNotificationEnabled)
        {
            _notifierManager.RegisterNotifier(new EmailNotifier(
                TaskContext.Instance().Config.NotificationConfig.SmtpServer,
                TaskContext.Instance().Config.NotificationConfig.SmtpPort,
                TaskContext.Instance().Config.NotificationConfig.SmtpUsername,
                TaskContext.Instance().Config.NotificationConfig.SmtpPassword,
                TaskContext.Instance().Config.NotificationConfig.FromEmail,
                TaskContext.Instance().Config.NotificationConfig.FromName,
                TaskContext.Instance().Config.NotificationConfig.ToEmail
            ));
        }

        // Telegram通知初始化
        if (TaskContext.Instance().Config.NotificationConfig.TelegramNotificationEnabled)
            _notifierManager.RegisterNotifier(new TelegramNotifier(
                NotifyHttpClient, // 使用共享的HttpClient
                TaskContext.Instance().Config.NotificationConfig.TelegramBotToken,
                TaskContext.Instance().Config.NotificationConfig.TelegramChatId,
                TaskContext.Instance().Config.NotificationConfig.TelegramApiBaseUrl
            ));
    }

    public void RefreshNotifiers()
    {
        _notifierManager.RemoveAllNotifiers();
        InitializeNotifiers();
    }

    public async Task<NotificationTestResult> TestNotifierAsync<T>() where T : INotifier
    {
        try
        {
            var notifier = _notifierManager.GetNotifier<T>();
            if (notifier == null)
            {
                return NotificationTestResult.Error("通知类型未启用");
            }

            var testData = new BaseNotificationData
            {
                Event = NotificationEvent.Test.Code,
                Result = NotificationEventResult.Success,
                Message = "这是一条测试通知信息",
            };
            if (TaskContext.Instance().IsInitialized)
            {
                testData.Screenshot = TaskControl.CaptureToRectArea().SrcBitmap;
            }

            await notifier.SendAsync(testData);
            return NotificationTestResult.Success();
        }
        catch (NotifierException ex)
        {
            return NotificationTestResult.Error(ex.Message);
        }
    }

    public async Task NotifyAllNotifiersAsync(BaseNotificationData notificationData)
    {
        var subscribeEventStr = TaskContext.Instance().Config.NotificationConfig.NotificationEventSubscribe;
        if (!string.IsNullOrEmpty(subscribeEventStr))
        {
            if (!subscribeEventStr.Contains(notificationData.Event))
            {
                return;
            }
        }

        try
        {
            if (TaskContext.Instance().Config.NotificationConfig.IncludeScreenShot)
            {
                var bitmap = TaskControl.CaptureGameBitmapNoRetry(TaskTriggerDispatcher.GlobalGameCapture);
                if (bitmap != null)
                {
                    notificationData.Screenshot = (Bitmap)bitmap.Clone();
                }
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "补充通知截图失败");
        }

        await _notifierManager.SendNotificationToAllAsync(notificationData);
    }

    public void NotifyAllNotifiers(BaseNotificationData notificationData)
    {
        Task.Run(() => NotifyAllNotifiersAsync(notificationData));
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        NotifyHttpClient.Dispose();
    }
}