using System.Drawing;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
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
        if (NotificationService.Instance().Config.NotificationConfig.IncludeScreenShot)// TODO: 这个获取方式是否合理？
        {
            var screenShot = (Bitmap)TaskControl.CaptureToRectArea().SrcBitmap.Clone();
            notificationData.Screenshot = screenShot;
        }
        NotificationService.Instance().NotifyAllNotifiers(notificationData);
    }
}

public class ScriptNotificationBuilderFactory
{
    public ScriptNotificationBuilder CreateWithScript(ScriptGroupProject script)
    {
        return new ScriptNotificationBuilder().WithEvent(NotificationEvent.Script).WithScript(script);
    }
}

public class TaskNotificationBuilderFactory
{
    public TaskNotificationBuilder CreateWithTask(TaskDetails task)
    {
        return new TaskNotificationBuilder().WithEvent(NotificationEvent.Task).WithTask(task);
    }
}

public class GeniusInvocationNotificationBuilderFactory
{
    public GeniusInvocationNotificationBuilder CreateWithGeniusInvocation(Duel geniusInvocation)
    {
        return new GeniusInvocationNotificationBuilder().WithEvent(NotificationEvent.GeniusInvocation).WithGeniusInvocation(geniusInvocation);
    }
}

public class DomainNotificationBuilderFactory
{
    public DomainNotificationBuilder CreateWithDomain(AutoDomainParam domain)
    {
        return new DomainNotificationBuilder().WithEvent(NotificationEvent.Domain).WithDomain(domain);
    }
}

