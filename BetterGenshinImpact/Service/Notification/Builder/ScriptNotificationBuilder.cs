using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Base;

namespace BetterGenshinImpact.Service.Notification.Builder;

public class ScriptNotificationBuilder : INotificationBuilder<ScriptNotificationBuilder, ScriptNotificationData>
{
    public ScriptNotificationBuilder WithScript(ScriptGroupProject script)
    {
        _notificationData.Script = script;
        return this;
    }
}