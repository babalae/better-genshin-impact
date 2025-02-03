using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Service.Notification;

/// <summary>
///     Notification
/// </summary>
[Serializable]
public partial class NotificationConfig : ObservableObject
{
    /// <summary>
    ///
    /// </summary>
    [ObservableProperty]
    private bool _webhookEnabled;

    /// <summary>
    ///
    /// </summary>
    [ObservableProperty]
    private string _webhookEndpoint = string.Empty;
    
    
    // [ObservableProperty]
    // private string _webhookEventSubscribe = string.Empty;

    /// <summary>
    /// 是否包含截图
    /// </summary>
    [ObservableProperty]
    private bool _includeScreenShot = false;
    
    /// <summary>
    /// windows uwp 通知是否启用
    /// </summary>
    [ObservableProperty]
    private bool _windowsUwpNotificationEnabled = false;
    
    
    // 飞书通知
    /// <summary>
    /// 飞书通知是否启用
    /// </summary>
    [ObservableProperty]
    private bool _feishuNotificationEnabled = false;
    
    
    /// <summary>
    /// 飞书通知地址
    /// </summary>
    [ObservableProperty]
    private string _feishuWebhookUrl = string.Empty;
    
    

}
