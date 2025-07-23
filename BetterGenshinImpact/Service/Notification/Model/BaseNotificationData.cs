using System;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using System.Text.Json.Serialization;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service.Notification.Converter;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service.Notification.Model;

public class BaseNotificationData
{
    /// <summary>
    /// NotificationEvent
    /// 事件类型
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// 事件结果
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotificationEventResult Result { get; set; }

    /// <summary>
    /// 事件发生时间
    /// </summary>
    [JsonConverter(typeof(DateTimeJsonConverter))]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// 事件发生时的截图
    /// </summary>
    [JsonConverter(typeof(ImageToBase64Converter))]
    public Image<Rgb24>? Screenshot { get; set; }

    /// <summary>
    /// 事件消息
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// 附加事件数据
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

    public void Send(string messageKey, params object[] args)
    {
        var messageService = App.GetService<NotificationMessageService>();
        Message = messageService != null ? messageService.GetMessage(messageKey, args) : messageKey;
        Send();
    }

    public void Success(string messageKey, params object[] args)
    {
        var messageService = App.GetService<NotificationMessageService>();
        Message = messageService != null ? messageService.GetMessage(messageKey, args) : messageKey;
        Result = NotificationEventResult.Success;
        Send();
    }

    public void Fail(string messageKey, params object[] args)
    {
        var messageService = App.GetService<NotificationMessageService>();
        Message = messageService != null ? messageService.GetMessage(messageKey, args) : messageKey;
        Result = NotificationEventResult.Fail;
        Send();
    }

    public void Error(string messageKey, params object[] args)
    {
        var messageService = App.GetService<NotificationMessageService>();
        Message = messageService != null ? messageService.GetErrorMessage(messageKey, args) : messageKey;
        Result = NotificationEventResult.Fail;
        Send();
    }

    public void Error(string messageKey, Exception exception)
    {
        var messageService = App.GetService<NotificationMessageService>();
        string localizedMessage = messageService != null ? messageService.GetErrorMessage(messageKey) : messageKey;
        Message = localizedMessage + Environment.NewLine + exception.Message + Environment.NewLine + exception.StackTrace;
        Result = NotificationEventResult.Fail;
        Send();
    }
}