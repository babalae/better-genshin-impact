using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Base;

namespace BetterGenshinImpact.Service.Notification.Builder;

public class GeniusInvocationNotificationBuilder : INotificationBuilder<GeniusInvocationNotificationBuilder, GeniusInvocationNotificationData>
{
    public GeniusInvocationNotificationBuilder WithGeniusInvocation(Duel geniusInvocation)
    {
        _notificationData.GeniusInvocation = geniusInvocation;
        return this;
    }
}

