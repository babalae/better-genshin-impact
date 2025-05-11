using System;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using System.Text.Json.Serialization;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service.Notification.Converter;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service.Notification.Model;

public class BaseNotificationData
{
    /// <summary>
    /// NotificationEvent
    /// 事件名称
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// 事件结果
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotificationEventResult Result { get; set; }

    /// <summary>
    /// 事件触发时间
    /// </summary>
    [JsonConverter(typeof(DateTimeJsonConverter))]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// 事件触发时的截图
    /// </summary>
    [JsonConverter(typeof(ImageToBase64Converter))]
    public Image<Rgb24>? Screenshot { get; set; }

    /// <summary>
    /// 事件消息
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// 额外的事件数据
    /// </summary>
    public object? Data { get; set; }

    public void Send()
    {
        try
        {
            NotificationService.Instance().NotifyAllNotifiers(this);
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "发送通知失败");
        }
    }

    public void Send(string message)
    {
        Message = message;
        Send();
    }

    public void Success(string message)
    {
        Message = message;
        Result = NotificationEventResult.Success;
        Send();
    }

    public void Fail(string message)
    {
        Message = message;
        Result = NotificationEventResult.Fail;
        Send();
    }

    public void Error(string message)
    {
        Message = message;
        Result = NotificationEventResult.Fail;
        Send();
    }

    public void Error(string message, Exception exception)
    {
        Message = message + Environment.NewLine + exception.Message + Environment.NewLine + exception.StackTrace;
        Result = NotificationEventResult.Fail;
        Send();
    }
}
