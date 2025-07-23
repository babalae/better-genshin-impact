using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.Service.Notification;

/// <summary>
/// Service for handling notification messages with localization support
/// </summary>
public class NotificationMessageService
{
    private readonly ILocalizationService? _localizationService;

    public NotificationMessageService()
    {
        _localizationService = App.GetService<ILocalizationService>();
    }

    /// <summary>
    /// Get a localized message for a notification
    /// </summary>
    /// <param name="key">The message key</param>
    /// <param name="args">Optional format arguments</param>
    /// <returns>The localized message</returns>
    public string GetMessage(string key, params object[] args)
    {
        return _localizationService != null ? _localizationService.GetString(key, args) : key;
    }

    /// <summary>
    /// Get a localized error message
    /// </summary>
    /// <param name="key">The error message key</param>
    /// <param name="args">Optional format arguments</param>
    /// <returns>The localized error message</returns>
    public string GetErrorMessage(string key, params object[] args)
    {
        return _localizationService != null ? _localizationService.GetString(key, args) : key;
    }

    // Common notification messages
    public string TestMessage => GetMessage("notification.message.test");
    public string ScreenshotFailedError => GetErrorMessage("notification.error.screenshotFailed");
    public string SendFailedError => GetErrorMessage("notification.error.sendFailed");
    public string DragonStartMessage => GetMessage("notification.message.dragonStart");
    public string ConfigGroupStartMessage => GetMessage("notification.message.configGroupStart");
    public string DragonEndMessage => GetMessage("notification.message.dragonEnd");
    public string ConfigGroupStartNamedMessage(string name) => GetMessage("notification.message.configGroupStartNamed", name);
    public string ConfigGroupEndNamedMessage(string name) => GetMessage("notification.message.configGroupEndNamed", name);
    public string TaskCancelManualMessage => GetMessage("notification.message.taskCancelManual");
    public string TaskCancelNormalMessage => GetMessage("notification.message.taskCancelNormal");
    public string DailyRewardClaimedMessage => GetMessage("notification.message.dailyRewardClaimed");
    public string DailyRewardUnclaimedMessage => GetMessage("notification.message.dailyRewardUnclaimed");
    public string DomainStartMessage => GetMessage("notification.message.domainStart");
    public string DomainEndMessage => GetMessage("notification.message.domainEnd");
    public string DomainRewardMessage => GetMessage("notification.message.domainReward");
    public string DomainRetryMessage => GetMessage("notification.message.domainRetry");
    public string TcgStartMessage => GetMessage("notification.message.tcgStart");
    public string TcgEndMessage => GetMessage("notification.message.tcgEnd");
    public string AlbumStartMessage => GetMessage("notification.message.albumStart");
    public string AlbumEndMessage => GetMessage("notification.message.albumEnd");
    public string AlbumErrorMessage => GetMessage("notification.message.albumError");
    public string JsCustomMessage(string message) => GetMessage("notification.message.jsCustom", message);
    public string JsErrorMessage(string error) => GetMessage("notification.message.jsError", error);
}