using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service.Notification.Model.Base;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Service.Notification.Model;

public class ScriptNotificationData : BaseNotificationData
{
    public ScriptGroupProject? Script { get; set; }
} 