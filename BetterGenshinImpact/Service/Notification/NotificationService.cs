using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Hosting;
using System;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.GameTask;
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
            await notifier.SendAsync(new TestNotificationData
            {
                Event = NotificationEvent.Test,
                Action = NotificationAction.Started,
                Conclusion = NotificationConclusion.Success,
                Message = "测试通知",
                // Screenshot = 
            });
            return NotificationTestResult.Success();
        }
        catch (NotifierException ex)
        {
            return NotificationTestResult.Error(ex.Message);
        }
    }

    public async Task NotifyAllNotifiersAsync(INotificationData notificationData)
    {
        await _notifierManager.SendNotificationToAllAsync(notificationData);
    }

    public void NotifyAllNotifiers(INotificationData notificationData)
    {
        Task.Run(() => NotifyAllNotifiersAsync(notificationData));
    }
}
