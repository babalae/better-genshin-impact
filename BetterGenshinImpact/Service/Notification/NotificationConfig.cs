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
    private string _barkApiEndpoint = string.Empty;

    [ObservableProperty] 
    private string _barkDeviceKeys = string.Empty;
    
    // Bark 通知附加参数配置
    
    /// <summary>
    /// 推送副标题
    /// </summary>
    [ObservableProperty]
    private string _barkSubtitle = string.Empty;
    
    /// <summary>
    /// 推送中断级别：critical(重要警告), active(默认值), timeSensitive(时效性通知), passive(仅添加到通知列表)
    /// </summary>
    [ObservableProperty]
    private string _barkLevel = "active";
    
    /// <summary>
    /// 通知声音
    /// </summary>
    [ObservableProperty]
    private string _barkSound = "minuet";
    
    /// <summary>
    /// 重要警告的通知音量，取值范围: 0-10
    /// </summary>
    [ObservableProperty]
    private int _barkVolume = 5;
    
    /// <summary>
    /// 推送角标，可以是任意数字
    /// </summary>
    [ObservableProperty]
    private int _barkBadge = 1;
    
    /// <summary>
    /// 通知铃声重复播放，1为开启
    /// </summary>
    [ObservableProperty]
    private string _barkCall = string.Empty;
    
    /// <summary>
    /// iOS14.5以下自动复制推送内容，1为开启
    /// </summary>
    [ObservableProperty]
    private string _barkAutoCopy = string.Empty;
    
    /// <summary>
    /// 复制推送时指定复制的内容
    /// </summary>
    [ObservableProperty]
    private string _barkCopy = string.Empty;
    
    /// <summary>
    /// 为推送设置自定义图标URL
    /// </summary>
    [ObservableProperty]
    private string _barkIcon = string.Empty;
    
    /// <summary>
    /// 对消息进行分组，推送将按group分组显示在通知中心中
    /// </summary>
    [ObservableProperty]
    private string _barkGroup = "default";
    
    /// <summary>
    /// 传1保存推送，传其他的不保存推送
    /// </summary>
    [ObservableProperty]
    private string _barkIsArchive = "1";
    
    /// <summary>
    /// 点击推送时跳转的URL
    /// </summary>
    [ObservableProperty]
    private string _barkUrl = string.Empty;
    
    /// <summary>
    /// 传"none"时，点击推送不会弹窗
    /// </summary>
    [ObservableProperty]
    private string _barkAction = string.Empty;
}