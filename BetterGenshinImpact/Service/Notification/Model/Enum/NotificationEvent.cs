using BetterGenshinImpact.Helpers;
﻿namespace BetterGenshinImpact.Service.Notification.Model.Enum;

public class NotificationEvent(string code, string msg)
{
    public static readonly NotificationEvent Test = new("notify.test", Lang.S["Service_12105_edb137"]);
    public static readonly NotificationEvent DomainReward = new("domain.reward", Lang.S["Service_12104_414375"]);
    public static readonly NotificationEvent DomainStart = new("domain.start", Lang.S["GameTask_10470_f23957"]);
    public static readonly NotificationEvent DomainEnd = new("domain.end", Lang.S["GameTask_10467_45a31a"]);
    public static readonly NotificationEvent DomainRetry = new("domain.retry", Lang.S["Service_12103_e44431"]);
    public static readonly NotificationEvent TaskCancel = new("task.cancel", Lang.S["Service_12102_150f66"]);
    public static readonly NotificationEvent TaskError = new("task.error", Lang.S["Service_12101_29fab6"]);
    public static readonly NotificationEvent GroupStart = new("group.start", Lang.S["Service_12100_0a733e"]);
    public static readonly NotificationEvent GroupEnd = new("group.end", Lang.S["Service_12099_a4f0dc"]);
    public static readonly NotificationEvent DragonStart = new("dragon.start", Lang.S["Service_12098_a6b203"]);
    public static readonly NotificationEvent DragonEnd = new("dragon.end", Lang.S["Service_12097_d04dfb"]);
    public static readonly NotificationEvent TcgStart = new("tcg.start", Lang.S["Service_12096_38ca8e"]);
    public static readonly NotificationEvent TcgEnd = new("tcg.end", Lang.S["Service_12095_05cd75"]);
    public static readonly NotificationEvent AlbumStart = new("album.start", Lang.S["GameTask_10986_5cd6b1"]);
    public static readonly NotificationEvent AlbumEnd = new("album.end", Lang.S["GameTask_10984_387998"]);
    public static readonly NotificationEvent AlbumError = new("album.error", Lang.S["Service_12094_c1586b"]);
    public static readonly NotificationEvent DailyReward = new("daily.reward", Lang.S["Service_12093_ad568b"]);
    public static readonly NotificationEvent JsCustom = new("js.custom", Lang.S["Service_12092_0f9fe1"]);
    public static readonly NotificationEvent JsError = new("js.error", Lang.S["Service_12091_4c2201"]);
    public static readonly NotificationEvent AutoEatStart = new("autoeat.start", Lang.S["Service_12090_0d9c37"]);
    public static readonly NotificationEvent AutoEatEnd = new("autoeat.end", Lang.S["Service_12089_0e9aac"]);
    public static readonly NotificationEvent AutoEatInfo = new("autoeat.info", Lang.S["Service_12088_0cb77c"]);
    
    public string Code { get; private set; } = code;
    public string Msg { get; private set; } = msg;
}
