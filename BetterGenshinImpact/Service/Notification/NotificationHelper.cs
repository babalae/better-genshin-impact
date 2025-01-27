using System.Drawing;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service.Notification.Builder;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Base;
using BetterGenshinImpact.Service.Notification.Model.Enum;

namespace BetterGenshinImpact.Service.Notification;

public class NotificationHelper
{
    public static void Notify(INotificationData notificationData)
    {
        if (TaskContext.Instance().Config.NotificationConfig.IncludeScreenShot)
        {
            var screenShot = (Bitmap)TaskControl.CaptureToRectArea().SrcBitmap.Clone();
            notificationData.Screenshot = screenShot;
        }
        NotificationService.Instance().NotifyAllNotifiers(notificationData);
    }
}

public class NotificationBuilderFactory
{
    public static ScriptNotificationBuilder CreateWith(ScriptGroupProject script)
    {
        return new ScriptNotificationBuilder().WithEvent(NotificationEvent.Script).WithScript(script);
    }

    public static TaskNotificationBuilder CreateWith(TaskDetails task)
    {
        return new TaskNotificationBuilder().WithEvent(NotificationEvent.Task).WithTask(task);
    }

    public static GeniusInvocationNotificationBuilder CreateWith(Duel geniusInvocation)
    {
        return new GeniusInvocationNotificationBuilder().WithEvent(NotificationEvent.GeniusInvocation).WithGeniusInvocation(geniusInvocation);
    }

    public static DomainNotificationBuilder CreateWith(AutoDomainParam domain)
    {
        return new DomainNotificationBuilder().WithEvent(NotificationEvent.Domain).WithDomain(domain);
    }
}
