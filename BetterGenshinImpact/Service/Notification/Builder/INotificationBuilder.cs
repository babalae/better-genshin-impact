using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using System.Drawing;

namespace BetterGenshinImpact.Service.Notification.Builder;

public abstract class INotificationBuilder<TBuilder, TNotificationData>
    where TBuilder : INotificationBuilder<TBuilder, TNotificationData>
    where TNotificationData : INotificationData, new()
{
    protected readonly TNotificationData _notificationData = new();

    protected TBuilder Self => (TBuilder)this;

    public TBuilder WithEvent(NotificationEvent notificationEvent)
    {
        _notificationData.Event = notificationEvent;
        return Self;
    }

    public TBuilder WithAction(NotificationAction action)
    {
        _notificationData.Action = action;
        return Self;
    }

    public TBuilder WithConclusion(NotificationConclusion? conclusion)
    {
        _notificationData.Conclusion = conclusion;
        return Self;
    }

    public TBuilder Started()
    {
        return WithAction(NotificationAction.Started);
    }

    private TBuilder Completed() // 一个completed任务，需要携带conclusion，故不暴露
    {
        return WithAction(NotificationAction.Completed);
    }

    public TBuilder Success()
    {
        return Completed()
            .WithConclusion(NotificationConclusion.Success);
    }
    public TBuilder PartialSuccess()
    {
        return Completed()
            .WithConclusion(NotificationConclusion.PartialSuccess);
    }

    public TBuilder Failure()
    {
        return Completed()
            .WithConclusion(NotificationConclusion.Failure);
    }

    public TBuilder Exception(string message)
    {
        return WithAction(NotificationAction.Exception)
            .WithMessage(message);
    }

    public TBuilder InProgress()
    {
        return WithAction(NotificationAction.InProgress);
    }

    public TBuilder WithScreenshot(Image? screenshot)
    {
        _notificationData.Screenshot = screenshot;
        return Self;
    }

    public TBuilder WithMessage(string message)
    {
        _notificationData.Message = message;
        return Self;
    }

    public TNotificationData Build()
    {
        return _notificationData;
    }
}