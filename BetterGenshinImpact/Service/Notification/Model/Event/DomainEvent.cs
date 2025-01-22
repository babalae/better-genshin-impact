using BetterGenshinImpact.Service.Notification.Model.Base;

namespace BetterGenshinImpact.Service.Notification.Model.Event;

public class DomainEvent : BaseEvent
{
    public DomainDetails? Domain { get; set; }
} 