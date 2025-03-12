using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.DirectoryServices.ActiveDirectory;

namespace BetterGenshinImpact.Service.Notification;

/// <summary>
/// 通知配置管理器
/// </summary>
[Serializable]
public partial class NotificationConfig : ObservableObject
{
    
        
    [ObservableProperty]
    private string _notificationEventSubscribe = string.Empty;
    
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
    


    /// <summary>
    /// 是否包含截图
    /// </summary>
    [ObservableProperty]
    private bool _includeScreenShot = true;
    
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
    
    
    // 企业微信通知
    /// <summary>
    /// 企业微信通知是否启用
    /// </summary>
    [ObservableProperty]
    private bool _workweixinNotificationEnabled = false;
    
    
    /// <summary>
    /// 企业微信通知通知地址
    /// </summary>
    [ObservableProperty]
    private string _workweixinWebhookUrl = string.Empty;

    [ObservableProperty]
    bool _webSocketNotificationEnabled = false;
    
    [ObservableProperty]
    private string _webSocketEndpoint = string.Empty;

    // Email 通知配置
    [ObservableProperty]
    private bool _emailNotificationEnabled = false;

    [ObservableProperty]
    private string _smtpServer = string.Empty;

    [ObservableProperty]
    private int _smtpPort;

    [ObservableProperty]
    private string _smtpUsername = string.Empty;

    [ObservableProperty]
    private string _smtpPassword = string.Empty;

    [ObservableProperty]
    private string _fromEmail = string.Empty;

    [ObservableProperty]
    private string _fromName = string.Empty;

    [ObservableProperty]
    private string _toEmail = string.Empty;
    
    /// <summary>
    /// Bark移动推送通知配置
    /// </summary>
    [ObservableProperty]
    private bool _barkNotificationEnabled = false;

    [ObservableProperty]
    private string _barkApiEndpoint = "https://api.day.app/push";

    [ObservableProperty] 
    private string _barkDeviceKeys = string.Empty;
    // private string[] _barkDeviceKeys = Array.Empty<string>();
}