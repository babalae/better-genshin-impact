using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Hosting;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.Notification;

public class NotificationService : IHostedService
{
    private static NotificationService? _instance;

    private static readonly HttpClient _httpClient = new();
    private readonly NotifierManager _notifierManager;
    public AllConfig Config { get; set; } //TODO:除了public以外还能怎么获取这个Config？

    public NotificationService(IConfigService configService, NotifierManager notifierManager)
    {
        Config = configService.Get();
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

    private StringContent TransformData(INotificationData notificationData)
    {
        // using object type here so it serializes the interface correctly
        var serializedData = JsonSerializer.Serialize<object>(notificationData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        return new StringContent(serializedData, Encoding.UTF8, "application/json");
    }

    private void InitializeNotifiers()
    {
        if (Config.NotificationConfig.WebhookEnabled)
        {
            _notifierManager.RegisterNotifier(new WebhookNotifier(_httpClient, Config.NotificationConfig.WebhookEndpoint));
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
            await notifier.SendNotificationAsync(TransformData(new TestNotificationData()));
            return NotificationTestResult.Success();
        }
        catch (NotifierException ex)
        {
            return NotificationTestResult.Error(ex.Message);
        }
    }

    public async Task NotifyAllNotifiersAsync(INotificationData notificationData)
    {
        await _notifierManager.SendNotificationToAllAsync(TransformData(notificationData));
    }

    public void NotifyAllNotifiers(INotificationData notificationData)
    {
        Task.Run(() => NotifyAllNotifiersAsync(notificationData));
    }
}
