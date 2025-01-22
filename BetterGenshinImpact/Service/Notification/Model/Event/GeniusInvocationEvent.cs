using BetterGenshinImpact.Service.Notification.Model.Base;

namespace BetterGenshinImpact.Service.Notification.Model.Event;

public class GeniusInvocationEvent : BaseEvent
{
    public GeniusInvocationDetails? GeniusInvocation { get; set; }
} 