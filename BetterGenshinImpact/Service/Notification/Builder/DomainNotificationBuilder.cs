using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Base;

namespace BetterGenshinImpact.Service.Notification.Builder;

public class DomainNotificationBuilder : INotificationBuilder<DomainNotificationBuilder, DomainNotificationData>
{
    public DomainNotificationBuilder WithDomain(AutoDomainParam domain)
    {
        _notificationData.Domain = domain;
        return this;
    }
}

