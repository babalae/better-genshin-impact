using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Service.Notification;

/// <summary>
/// 通知配置管理器
/// </summary>
[Serializable]
public partial class NotificationConfig : ObservableObject
{
    /// <summary>
    /// 传"none"时，点击推送不会弹窗
    /// </summary>
    [ObservableProperty] private string _barkAction = string.Empty;

    [ObservableProperty] private string _barkApiEndpoint = string.Empty;

    /// <summary>
    /// iOS14.5以下自动复制推送内容，1为开启
    /// </summary>
    [ObservableProperty] private string _barkAutoCopy = string.Empty;

    /// <summary>
    /// 推送角标，可以是任意数字
    /// </summary>
    [ObservableProperty] private int _barkBadge = 1;

    /// <summary>
    /// 通知铃声重复播放，1为开启
    /// </summary>
    [ObservableProperty] private string _barkCall = string.Empty;

    [ObservableProperty] private string _barkCiphertext = string.Empty;

    /// <summary>
    /// 复制推送时指定复制的内容
    /// </summary>
    [ObservableProperty] private string _barkCopy = string.Empty;

    [ObservableProperty] private string _barkDeviceKeys = string.Empty;

    /// <summary>
    /// 对消息进行分组，推送将按group分组显示在通知中心中
    /// </summary>
    [ObservableProperty] private string _barkGroup = "default";

    /// <summary>
    ///     为推送设置自定义图标URL
    /// </summary>
    [ObservableProperty] private string _barkIcon = string.Empty;

    /// <summary>
    /// 传1保存推送，传其他的不保存推送
    /// </summary>
    [ObservableProperty] private string _barkIsArchive = "1";

    /// <summary>
    ///     推送中断级别：critical(重要警告), active(默认值), timeSensitive(时效性通知), passive(仅添加到通知列表)
    /// </summary>
    [ObservableProperty] private string _barkLevel = "active";

    /// <summary>
    ///     Bark移动推送通知配置
    /// </summary>
    [ObservableProperty] private bool _barkNotificationEnabled;

    /// <summary>
    ///     通知声音
    /// </summary>
    [ObservableProperty] private string _barkSound = "minuet";

    // Bark 通知附加参数配置

    /// <summary>
    ///     推送副标题
    /// </summary>
    [ObservableProperty] private string _barkSubtitle = string.Empty;

    /// <summary>
    /// 点击推送时跳转的URL
    /// </summary>
    [ObservableProperty] private string _barkUrl = string.Empty;

    /// <summary>
    /// 重要警告的通知音量，取值范围: 0-10
    /// </summary>
    [ObservableProperty] private int _barkVolume = 5;

    // Email 通知配置
    [ObservableProperty] private bool _emailNotificationEnabled;


    // 飞书通知
    /// <summary>
    ///     飞书通知是否启用
    /// </summary>
    [ObservableProperty] private bool _feishuNotificationEnabled;


    /// <summary>
    ///     飞书通知地址
    /// </summary>
    [ObservableProperty] private string _feishuWebhookUrl = string.Empty;

    [ObservableProperty] private string _fromEmail = string.Empty;

    [ObservableProperty] private string _fromName = string.Empty;


    /// <summary>
    ///     是否包含截图
    /// </summary>
    [ObservableProperty] private bool _includeScreenShot = true;


    [ObservableProperty] private string _notificationEventSubscribe = string.Empty;

    [ObservableProperty] private string _smtpPassword = string.Empty;

    [ObservableProperty] private int _smtpPort;

    [ObservableProperty] private string _smtpServer = string.Empty;

    [ObservableProperty] private string _smtpUsername = string.Empty;

    /// <summary>
    ///     Telegram API基础URL(可选，留空使用官方API)
    /// </summary>
    [ObservableProperty] private string _telegramApiBaseUrl = string.Empty;

    /// <summary>
    ///     Telegram机器人Token
    /// </summary>
    [ObservableProperty] private string _telegramBotToken = string.Empty;

    /// <summary>
    ///     Telegram聊天ID
    /// </summary>
    [ObservableProperty] private string _telegramChatId = string.Empty;

    // Telegram通知
    /// <summary>
    ///     Telegram通知是否启用
    /// </summary>
    [ObservableProperty] private bool _telegramNotificationEnabled;

    [ObservableProperty] private string _toEmail = string.Empty;

    /// <summary>
    /// </summary>
    [ObservableProperty] private bool _webhookEnabled;

    /// <summary>
    /// </summary>
    [ObservableProperty] private string _webhookEndpoint = string.Empty;
    

    [ObservableProperty] private string _webhookSendTo = string.Empty; // 修改属性名
    
    [ObservableProperty] private string _webSocketEndpoint = string.Empty;

    [ObservableProperty] private bool _webSocketNotificationEnabled;

    /// <summary>
    /// windows uwp 通知是否启用
    /// </summary>
    [ObservableProperty] private bool _windowsUwpNotificationEnabled;


    // 企业微信通知
    /// <summary>
    ///     企业微信通知是否启用
    /// </summary>
    [ObservableProperty] private bool _workweixinNotificationEnabled;


    /// <summary>
    ///     企业微信通知通知地址
    /// </summary>
    [ObservableProperty] private string _workweixinWebhookUrl = string.Empty;
}