using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Base;

namespace BetterGenshinImpact.Service.Notification.Builder;

public class TaskNotificationBuilder : INotificationBuilder<TaskNotificationBuilder, TaskNotificationData>
{
    public TaskNotificationBuilder WithTask(TaskDetails task)
    {
        _notificationData.Task = task;
        return this;
    }
}