using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Hosting;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service.Notification.Model.Enum;

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

    public static NotificationService Instance()
    {
        if (_instance == null)
        {
            throw new Exception("Not instantiated");
        }

        return _instance;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


    private void InitializeNotifiers()
    {
        if (TaskContext.Instance().Config.NotificationConfig.WebhookEnabled)
        {
            _notifierManager.RegisterNotifier(new WebhookNotifier(NotifyHttpClient, TaskContext.Instance().Config.NotificationConfig.WebhookEndpoint));
        }

        if (TaskContext.Instance().Config.NotificationConfig.WindowsUwpNotificationEnabled)
        {
            _notifierManager.RegisterNotifier(new WindowsUwpNotifier());
        }

        if (TaskContext.Instance().Config.NotificationConfig.FeishuNotificationEnabled)
        {
            _notifierManager.RegisterNotifier(new FeishuNotifier(NotifyHttpClient, TaskContext.Instance().Config.NotificationConfig.FeishuWebhookUrl));
        }

        if (TaskContext.Instance().Config.NotificationConfig.WorkweixinNotificationEnabled)
        {
            _notifierManager.RegisterNotifier(new WorkWeixinNotifier(NotifyHttpClient, TaskContext.Instance().Config.NotificationConfig.WorkweixinWebhookUrl));
        }
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

        await _notifierManager.SendNotificationToAllAsync(notificationData);
    }

    public void NotifyAllNotifiers(BaseNotificationData notificationData)
    {
        Task.Run(() => NotifyAllNotifiersAsync(notificationData));
    }
}