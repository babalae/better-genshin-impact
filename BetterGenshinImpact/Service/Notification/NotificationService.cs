using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
using System.Net.Http;
	@@ -11,143 +12,265 @@
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Linq;

namespace BetterGenshinImpact.Service.Notification;

/// <summary>
/// 通知服务：高度可配置、可扩展的多通道通知管理系统
/// 
/// 核心设计原则：
/// - 模块化架构
/// - 动态通知通道配置
/// - 高可靠性
/// - 最小性能开销
/// </summary>
public class NotificationService : IHostedService, IDisposable
{
    // 单例实例管理
    private static NotificationService? _instance;

    // 线程安全的HTTP客户端
    private static readonly HttpClient NotifyHttpClient = new();

    // 通知管理器
    private readonly NotifierManager _notifierManager;

    // 日志记录器
    private readonly ILogger<NotificationService> _logger;

    // 通知去重与限流机制
    private readonly ConcurrentDictionary<string, DateTime> _lastNotificationTimes = new();

    // 配置上下文
    private readonly TaskContext _taskContext;

    // 最小通知间隔（秒）
    private const int MinNotificationInterval = 5;

    /// <summary>
    /// 构造函数：依赖注入关键组件
    /// </summary>
    public NotificationService(
        NotifierManager notifierManager, 
        ILogger<NotificationService> logger, 
        TaskContext taskContext)
    {
        _notifierManager = notifierManager ?? throw new ArgumentNullException(nameof(notifierManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _taskContext = taskContext ?? throw new ArgumentNullException(nameof(taskContext));

        // 线程安全的单例初始化
        Interlocked.CompareExchange(ref _instance, this, null);

        // 初始化通知通道
        InitializeNotifiers();
    }

    /// <summary>
    /// 获取服务单例实例
    /// </summary>
    /// <returns>NotificationService实例</returns>
    /// <exception cref="InvalidOperationException">服务未初始化时抛出</exception>
    public static NotificationService Instance()
    {
        return _instance ?? throw new InvalidOperationException("通知服务未初始化");
    }

    /// <summary>
    /// 初始化通知通道
    /// 支持多种通知方式的动态注册
    /// </summary>
    private void InitializeNotifiers()
    {
        try
        {
            // Webhook通知初始化
            if (_taskContext.Config.NotificationConfig.WebhookEnabled)
            {
                _notifierManager.RegisterNotifier(new WebhookNotifier(
                    NotifyHttpClient, 
                    _taskContext.Config.NotificationConfig.WebhookEndpoint
                ));
            }

            // Windows UWP通知初始化
            if (_taskContext.Config.NotificationConfig.WindowsUwpNotificationEnabled)
            {
                _notifierManager.RegisterNotifier(new WindowsUwpNotifier());
            }

            // 飞书通知初始化
            if (_taskContext.Config.NotificationConfig.FeishuNotificationEnabled)
            {
                _notifierManager.RegisterNotifier(new FeishuNotifier(
                    NotifyHttpClient, 
                    _taskContext.Config.NotificationConfig.FeishuWebhookUrl
                ));
            }

            // 企业微信通知初始化
            if (_taskContext.Config.NotificationConfig.WorkweixinNotificationEnabled)
            {
                _notifierManager.RegisterNotifier(new WorkWeixinNotifier(
                    NotifyHttpClient, 
                    _taskContext.Config.NotificationConfig.WorkweixinWebhookUrl
                ));
            }

            // WebSocket通知初始化
            if (_taskContext.Config.NotificationConfig.WebSocketNotificationEnabled)
            {
                var jsonSerializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                };
                var cts = new CancellationTokenSource();
                _notifierManager.RegisterNotifier(new WebSocketNotifier(
                    _taskContext.Config.NotificationConfig.WebSocketEndpoint, 
                    jsonSerializerOptions, 
                    cts
                ));
            }

            // Bark通知初始化
            if (_taskContext.Config.NotificationConfig.BarkNotificationEnabled)
            {
                _notifierManager.RegisterNotifier(new BarkNotifier(
                    _taskContext.Config.NotificationConfig.BarkDeviceKeys,
                    _taskContext.Config.NotificationConfig.BarkApiEndpoint
                ));
            }

            // 电子邮件通知初始化
            if (_taskContext.Config.NotificationConfig.EmailNotificationEnabled)
            {
                _notifierManager.RegisterNotifier(new EmailNotifier(
                    _taskContext.Config.NotificationConfig.SmtpServer,
                    _taskContext.Config.NotificationConfig.SmtpPort,
                    _taskContext.Config.NotificationConfig.SmtpUsername,
                    _taskContext.Config.NotificationConfig.SmtpPassword,
                    _taskContext.Config.NotificationConfig.FromEmail,
                    _taskContext.Config.NotificationConfig.FromName,
                    _taskContext.Config.NotificationConfig.ToEmail
                ));
            }

            _logger.LogInformation("通知服务通道初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通知服务通道初始化失败");
            throw;
        }
    }

    /// <summary>
    /// 刷新通知通道配置
    /// 支持运行时动态重新配置
    /// </summary>
    public void RefreshNotifiers()
    {
        try
        {
            // 安全地移除所有现有通知器
            _notifierManager.RemoveAllNotifiers();

            // 重新初始化通知通道
            InitializeNotifiers();

            _logger.LogInformation("通知服务通道已成功刷新");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通知服务通道刷新失败");
            throw;
        }
    }

    /// <summary>
    /// 测试特定类型的通知器
    /// </summary>
    /// <typeparam name="T">通知器类型</typeparam>
    /// <returns>通知测试结果</returns>
    public async Task<NotificationTestResult> TestNotifierAsync<T>() where T : INotifier
    {
        try
        {
            var notifier = _notifierManager.GetNotifier<T>();
            if (notifier == null)
            {
                _logger.LogWarning($"尝试测试未启用的通知类型: {typeof(T).Name}");
                return NotificationTestResult.Error("通知类型未启用");
            }

            // 创建测试通知数据
            var testData = new BaseNotificationData
            {
                Event = NotificationEvent.Test.Code,
                Result = NotificationEventResult.Success,
                Message = "这是一条测试通知信息",
            };

            // 尝试添加截图（如果可能）
            if (_taskContext.IsInitialized)
            {
                testData.Screenshot = TaskControl.CaptureToRectArea().SrcBitmap;
            }

            // 使用超时控制发送测试通知
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await notifier.SendAsync(testData, cts.Token);

            _logger.LogInformation($"通知器 {typeof(T).Name} 测试成功");
            return NotificationTestResult.Success();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning($"通知器 {typeof(T).Name} 测试超时");
            return NotificationTestResult.Error("通知发送超时");
        }
        catch (NotifierException ex)
        {
            _logger.LogError(ex, $"通知器 {typeof(T).Name} 测试失败");
            return NotificationTestResult.Error(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"通知器 {typeof(T).Name} 测试发生未预期错误");
            return NotificationTestResult.Error("测试发生未知错误");
        }
    }

    /// <summary>
    /// 通知所有已配置的通知通道
    /// 支持事件订阅和截图策略
    /// </summary>
    /// <param name="notificationData">通知数据</param>
    public async Task NotifyAllNotifiersAsync(BaseNotificationData notificationData)
    {
        // 事件订阅过滤
        var subscribeEventStr = _taskContext.Config.NotificationConfig.NotificationEventSubscribe;
        if (!string.IsNullOrEmpty(subscribeEventStr) && 
            !subscribeEventStr.Contains(notificationData.Event))
        {
            _logger.LogDebug($"事件 {notificationData.Event} 未订阅，跳过通知");
            return;
        }

        // 通知去重检查
        if (ShouldSuppressNotification(notificationData))
        {
            _logger.LogDebug($"通知 {notificationData.Event} 被抑制，未超过最小通知间隔");
            return;
        }

        try
        {
            // 尝试添加截图
            if (_taskContext.Config.NotificationConfig.IncludeScreenShot)
            {
                var bitmap = TaskControl.CaptureGameBitmapNoRetry(TaskTriggerDispatcher.GlobalGameCapture);
                if (bitmap != null)
	@@ -156,16 +279,76 @@ public async Task NotifyAllNotifiersAsync(BaseNotificationData notificationData)
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "补充通知截图失败");
        }

        // 并行发送通知
        try
        {
            var notificationTasks = _notifierManager.GetNotifiers()
                .Select(notifier => notifier.SendAsync(notificationData))
                .ToList();

            await Task.WhenAll(notificationTasks);

            // 更新最后通知时间
            _lastNotificationTimes[notificationData.Event] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通知发送过程中发生错误");
        }
    }

    /// <summary>
    /// 异步触发通知（不阻塞当前线程）
    /// </summary>
    /// <param name="notificationData">通知数据</param>
    public void NotifyAllNotifiers(BaseNotificationData notificationData)
    {
        _ = Task.Run(() => NotifyAllNotifiersAsync(notificationData));
    }

    /// <summary>
    /// 检查是否应抑制通知
    /// 防止短时间内重复通知
    /// </summary>
    private bool ShouldSuppressNotification(BaseNotificationData notificationData)
    {
        var key = notificationData.Event;
        var now = DateTime.UtcNow;

        return _lastNotificationTimes.TryGetValue(key, out var lastTime) 
               && (now - lastTime).TotalSeconds < MinNotificationInterval;
    }

    /// <summary>
    /// 服务启动
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("通知服务正在启动");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 服务停止
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("通知服务正在停止");
        _notifierManager.RemoveAllNotifiers();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        NotifyHttpClient.Dispose();
        _logger.LogInformation("通知服务资源已释放");
    }
}
