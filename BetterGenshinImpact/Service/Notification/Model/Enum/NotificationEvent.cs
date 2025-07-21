using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.Service.Notification.Model.Enum;

public class NotificationEvent(string code, string msgKey)
{
    public static readonly NotificationEvent Test = new("notify.test", "notification.event.test");
    public static readonly NotificationEvent DomainReward = new("domain.reward", "notification.event.domainReward");
    public static readonly NotificationEvent DomainStart = new("domain.start", "notification.event.domainStart");
    public static readonly NotificationEvent DomainEnd = new("domain.end", "notification.event.domainEnd");
    public static readonly NotificationEvent DomainRetry = new("domain.retry", "notification.event.domainRetry");
    public static readonly NotificationEvent TaskCancel = new("task.cancel", "notification.event.taskCancel");
    public static readonly NotificationEvent TaskError = new("task.error", "notification.event.taskError");
    public static readonly NotificationEvent GroupStart = new("group.start", "notification.event.groupStart");
    public static readonly NotificationEvent GroupEnd = new("group.end", "notification.event.groupEnd");
    public static readonly NotificationEvent DragonStart = new("dragon.start", "notification.event.dragonStart");
    public static readonly NotificationEvent DragonEnd = new("dragon.end", "notification.event.dragonEnd");
    public static readonly NotificationEvent TcgStart = new("tcg.start", "notification.event.tcgStart");
    public static readonly NotificationEvent TcgEnd = new("tcg.end", "notification.event.tcgEnd");
    public static readonly NotificationEvent AlbumStart = new("album.start", "notification.event.albumStart");
    public static readonly NotificationEvent AlbumEnd = new("album.end", "notification.event.albumEnd");
    public static readonly NotificationEvent AlbumError = new("album.error", "notification.event.albumError");
    public static readonly NotificationEvent DailyReward = new("daily.reward", "notification.event.dailyReward");
    public static readonly NotificationEvent JsCustom = new("js.custom", "notification.event.jsCustom");
    public static readonly NotificationEvent JsError = new("js.error", "notification.event.jsError");
    
    public string Code { get; private set; } = code;
    public string MsgKey { get; private set; } = msgKey;
    
    /// <summary>
    /// Gets the localized message for this notification event
    /// </summary>
    /// <returns>Localized message string</returns>
    public string GetLocalizedMessage()
    {
        var messageService = App.GetService<NotificationMessageService>();
        return messageService != null ? messageService.GetMessage(MsgKey) : MsgKey;
    }
}
