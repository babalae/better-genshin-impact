using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.Service.Notification.Model.Base;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Service.Notification.Model;

public class DomainNotificationData : BaseNotificationData
{
    public AutoDomainParam? Domain { get; set; }
} 