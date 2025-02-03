namespace BetterGenshinImpact.Service.Notification.Model.Enum;

public class NotificationEvent(string code, string msg)
{
    public static NotificationEvent Test = new("notify.test", "测试通知");
    
    
    public static NotificationEvent DomainReward = new("domain.reward", "测试通知");
    
    public string Code { get; private set; } = code;
    public string Msg { get; private set; } = msg;
}
