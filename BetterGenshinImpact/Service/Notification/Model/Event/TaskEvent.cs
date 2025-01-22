using BetterGenshinImpact.Service.Notification.Model.Base;

namespace BetterGenshinImpact.Service.Notification.Model.Event;

public class TaskEvent : BaseEvent
{
    public TaskDetails? Task { get; set; }
} 