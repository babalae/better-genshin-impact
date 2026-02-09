using BetterGenshinImpact.Helpers;
﻿namespace BetterGenshinImpact.Service.Notification.Model;

public class NotificationTestResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;

    public static NotificationTestResult Success()
    {
        return new NotificationTestResult { IsSuccess = true, Message = Lang.S["Service_12087_4e3605"] };
    }

    public static NotificationTestResult Error(string message)
    {
        return new NotificationTestResult { IsSuccess = false, Message = message };
    }
}
