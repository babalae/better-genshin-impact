using System.Drawing;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;

namespace BetterGenshinImpact.Service.Notification.Builder;

public class TaskNotificationBuilder : INotificationDataBuilder
{
    private TaskNotificationData _notificationData = new();

    public TaskNotificationBuilder AddEvent(NotificationEvent notificationEvent)
    {
        _notificationData.Event = notificationEvent;
        return this;
    }

    public TaskNotificationBuilder AddAction(NotificationAction notificationAction)
    {
        _notificationData.Action = notificationAction;
        return this;
    }

    public TaskNotificationBuilder AddConclusion(NotificationConclusion? conclusion)
    {
        _notificationData.Conclusion = conclusion;
        return this;
    }

    public TaskNotificationBuilder GeniusInvocation()
    {
        return AddEvent(NotificationEvent.GeniusInvocation);
    }

    public TaskNotificationBuilder Domain()
    {
        return AddEvent(NotificationEvent.Domain);
    }

    public TaskNotificationBuilder Started()
    {
        return AddAction(NotificationAction.Started);
    }

    public TaskNotificationBuilder Completed()
    {
        return AddAction(NotificationAction.Completed);
    }

    public TaskNotificationBuilder Progress()
    {
        return AddAction(NotificationAction.Progress);
    }

    public TaskNotificationBuilder Success()
    {
        return AddAction(NotificationAction.Completed)
            .AddConclusion(NotificationConclusion.Success);
    }

    public TaskNotificationBuilder Failure()
    {
        return AddAction(NotificationAction.Completed)
            .AddConclusion(NotificationConclusion.Failure);
    }

    public TaskNotificationBuilder Cancelled()
    {
        return AddAction(NotificationAction.Completed)
            .AddConclusion(NotificationConclusion.Cancelled);
    }

    public TaskNotificationBuilder AddScreenshot(Image screenshot)
    {
        _notificationData.Screenshot = screenshot;
        return this;
    }

    public TaskNotificationBuilder AddTask(object task)
    {
        _notificationData.Task = task;
        return this;
    }

    public INotificationData Build()
    {
        return _notificationData;
    }

    public void Send()
    {
        NotificationService.Instance().NotifyAllNotifiers(Build());
    }
}
