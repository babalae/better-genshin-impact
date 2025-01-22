namespace BetterGenshinImpact.Service.Notification.Model.Enum;

// 希望一个Started对应且只能对应一个Completed
// 但enforce这个是否必要？
public enum NotificationAction
{
    Started,
    Completed,
    InProgress,
    Exception,
}
