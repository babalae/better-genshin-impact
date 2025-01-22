using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.Service.Notification.Model.Base;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Service.Notification.Model;

public class GeniusInvocationNotificationData : INotificationData
{
    public Duel? GeniusInvocation { get; set; }
}