namespace BetterGenshinImpact.Service.Notification.Model.Enum;

public class NotificationEvent(string code, string msg)
{
    public static readonly NotificationEvent Test = new("notify.test", "测试通知");
    public static readonly NotificationEvent DomainReward = new("domain.reward", "自动秘境奖励");
    public static readonly NotificationEvent DomainStart = new("domain.start", "自动秘境启动");
    public static readonly NotificationEvent DomainEnd = new("domain.end", "自动秘境结束");
    public static readonly NotificationEvent DomainRetry = new("domain.retry", "自动秘境重试");
    public static readonly NotificationEvent TaskCancel = new("task.cancel", "任务启动");
    public static readonly NotificationEvent TaskError = new("task.error", "任务错误");
    public static readonly NotificationEvent GroupStart = new("group.start", "配置组启动");
    public static readonly NotificationEvent GroupEnd = new("group.end", "配置组结束");
    public static readonly NotificationEvent DragonStart = new("dragon.start", "一条龙启动");
    public static readonly NotificationEvent DragonEnd = new("dragon.end", "一条龙结束");
    public static readonly NotificationEvent TcgStart = new("tcg.start", "七圣召唤启动");
    public static readonly NotificationEvent TcgEnd = new("tcg.end", "七圣召唤结束");
    public static readonly NotificationEvent AlbumStart = new("album.start", "自动音游专辑启动");
    public static readonly NotificationEvent AlbumEnd = new("album.end", "自动音游专辑结束");
    public static readonly NotificationEvent AlbumError = new("album.error", "自动音游专辑错误");
    public static readonly NotificationEvent DailyReward = new("daily.reward", "检查每日奖励领取状态");
    public static readonly NotificationEvent JsCustom = new("js.custom", "JS自定义事件");
    public static readonly NotificationEvent JsError = new("js.error", "JS运行时错误");
    public static readonly NotificationEvent AutoEatStart = new("autoeat.start", "自动吃药启动");
    public static readonly NotificationEvent AutoEatEnd = new("autoeat.end", "自动吃药结束");
    public static readonly NotificationEvent AutoEatInfo = new("autoeat.info", "自动吃药信息");
    
    public string Code { get; private set; } = code;
    public string Msg { get; private set; } = msg;
}
