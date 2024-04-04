using System;
using System.Drawing;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service.Notification.Builder;
using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notification;

public class NotificationHelper
{
    public static void Notify(INotificationData notificationData)
    {
        NotificationService.Instance().NotifyAllNotifiers(notificationData);
    }

    public static void SendTaskNotificationUsing(Func<TaskNotificationBuilder, INotificationData> builderFunc)
    {
        var builder = new TaskNotificationBuilder();
        Notify(builderFunc(builder));
    }

    public static void SendTaskNotificationWithScreenshotUsing(Func<TaskNotificationBuilder, INotificationData> builderFunc)
    {
        var builder = new TaskNotificationBuilder();
        var screenShot = (Bitmap)TaskControl.GetContentFromDispatcher().SrcBitmap.Clone();
        Notify(builderFunc(builder.WithScreenshot(screenShot)));
    }
}
