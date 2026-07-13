using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class Notification
{
  private readonly AllConfig _config = TaskContext.Instance().Config; 
    private readonly ILogger<Notification> _logger = App.GetLogger<Notification>();
    private readonly TimeSpan _timeWindow = TimeSpan.FromMinutes(1);
    private readonly int _maxNotifications = 5;
    private readonly Queue<DateTime> _callRecords = new();
    
    private static readonly string[] _forbiddenPatterns =
    [
        "<script>", "http://", "https://"
    ];
    
    private bool CheckNotificationPermission()
    {
        try
        {
            var currentProject = TaskContext.Instance().CurrentScriptProject;
            return _config.NotificationConfig.JsNotificationEnabled &&
                   (currentProject?.AllowJsNotification ?? true);
        }
        catch
        {
            return false;
        }
    }

    private bool ValidateContent(string message)   // 不允许发送超过 500 字符的消息
    {
        if (message.Length > 500) return false;
    
        return !_forbiddenPatterns
            .Any(p => message.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    
    private bool CheckRateLimit()   // 不允许频繁发送消息
    {
        var now = DateTime.Now;
        while (_callRecords.TryPeek(out var time) && now - time > _timeWindow)
        {
            _callRecords.Dequeue();
        }

        if (_callRecords.Count >= _maxNotifications)
        {
            return false;
        }

        _callRecords.Enqueue(now);
        return true;
    }
    
    /// <summary>
    /// 发送成功通知
    /// </summary>
    /// <param name="message">通知消息</param>
    public void Send(string message)
    {
        if (!CheckNotificationPermission())
        {
            _logger.LogWarning("JS 通知关闭，消息被拦截: " + message);
            return;
        }
        if (!CheckRateLimit())
        {
            _logger.LogWarning("通知频率超限，消息被拦截: " + message);
            return;
        }
        if (!ValidateContent(message))
        {
            _logger.LogWarning("通知内容违规，消息被拦截: " + message);
            return;
        }
        Notify.Event(NotificationEvent.JsCustom).Send(message);
        _logger.LogInformation("通知发送成功：" + message);
    }

    /// <summary>
    /// 发送错误通知
    /// </summary>
    /// <param name="message">通知消息</param>
    public void Error(string message)
    {
        if (!CheckNotificationPermission())
        {
            _logger.LogWarning("JS 通知关闭，消息被拦截: " + message);
            return;
        }
        if (!CheckRateLimit())
        {
            _logger.LogWarning("通知频率超限，消息被拦截: " + message);
            return;
        }

        if (!ValidateContent(message))
        {
            _logger.LogWarning("通知内容违规，消息被拦截: " + message);
            return;
        }
        Notify.Event(NotificationEvent.JsError).Error(message);
        _logger.LogInformation("错误通知发送成功：" + message);
    }
}