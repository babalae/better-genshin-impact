using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service.Notification.Model.Base;

namespace BetterGenshinImpact.Service.Notification.Model.Event;

public class ScriptEvent : BaseEvent
{
    public ScriptGroupProject? Script { get; set; }
} 