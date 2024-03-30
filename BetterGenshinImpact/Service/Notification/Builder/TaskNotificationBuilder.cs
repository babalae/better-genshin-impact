using System.Drawing;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;

namespace BetterGenshinImpact.Service.Notification.Builder;

public class TaskNotificationBuilder : INotificationDataBuilder<TaskNotificationData>
{
    private readonly TaskNotificationData _notificationData = new();

    public TaskNotificationBuilder WithEvent(NotificationEvent notificationEvent)
    {
        _notificationData.Event = notificationEvent;
        return this;
    }

    public TaskNotificationBuilder WithAction(NotificationAction notificationAction)
    {
        _notificationData.Action = notificationAction;
        return this;
    }

    public TaskNotificationBuilder WithConclusion(NotificationConclusion? conclusion)
    {
        _notificationData.Conclusion = conclusion;
        return this;
    }

    public TaskNotificationBuilder GeniusInvocation()
    {
        return WithEvent(NotificationEvent.GeniusInvocation);
    }

    public TaskNotificationBuilder Domain()
    {
        return WithEvent(NotificationEvent.Domain);
    }

    public TaskNotificationBuilder Started()
    {
        return WithAction(NotificationAction.Started);
    }

    public TaskNotificationBuilder Completed()
    {
        return WithAction(NotificationAction.Completed);
    }

    public TaskNotificationBuilder Progress()
    {
        return WithAction(NotificationAction.Progress);
    }

    public TaskNotificationBuilder Success()
    {
        return WithAction(NotificationAction.Completed)
            .WithConclusion(NotificationConclusion.Success);
    }

    public TaskNotificationBuilder Failure()
    {
        return WithAction(NotificationAction.Completed)
            .WithConclusion(NotificationConclusion.Failure);
    }

    public TaskNotificationBuilder Cancelled()
    {
        return WithAction(NotificationAction.Completed)
            .WithConclusion(NotificationConclusion.Cancelled);
    }

    public TaskNotificationBuilder WithScreenshot(Image? screenshot)
    {
        _notificationData.Screenshot = screenshot;
        return this;
    }

    public TaskNotificationBuilder AddTask(object task)
    {
        _notificationData.Task = task;
        return this;
    }

    public TaskNotificationData Build()
    {
        return _notificationData;
    }
}
