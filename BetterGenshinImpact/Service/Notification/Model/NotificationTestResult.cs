using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.Service.Notification.Model;

public class NotificationTestResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;

    public static NotificationTestResult Success()
    {
        var localizationService = App.GetService<ILocalizationService>();
        var message = localizationService != null ? localizationService.GetString("notification.message.testSuccess") : "通知成功";
        return new NotificationTestResult { IsSuccess = true, Message = message };
    }

    public static NotificationTestResult Error(string message)
    {
        var localizationService = App.GetService<ILocalizationService>();
        // Try to use the message as a localization key if it looks like one
        if (message != null && message.Contains('.') && !message.Contains(' '))
        {
            var localizedMessage = localizationService?.GetString(message);
            if (localizedMessage != null && !localizedMessage.StartsWith("[KEY_NOT_FOUND:"))
            {
                return new NotificationTestResult { IsSuccess = false, Message = localizedMessage };
            }
        }
        return new NotificationTestResult { IsSuccess = false, Message = message };
    }
}
