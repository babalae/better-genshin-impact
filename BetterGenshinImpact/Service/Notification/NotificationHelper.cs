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

    public static void NotifyUsing(Func<TaskNotificationBuilder, INotificationData> func)
    {
        var screenShot = (Bitmap)TaskControl.GetContentFromDispatcher().SrcBitmap.Clone();
        Notify(func(new TaskNotificationBuilder().AddScreenshot(screenShot)));
    }
}
