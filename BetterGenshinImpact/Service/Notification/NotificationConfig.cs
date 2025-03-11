using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BetterGenshinImpact.Service.Notification;

/// <summary>
/// 通知配置管理器
/// </summary>
[Serializable]
public partial class NotificationConfig : ObservableObject
{
    #region 通知事件订阅配置
    /// <summary>
    /// 通知事件订阅配置
    /// 提供细粒度的事件过滤和路由机制
    /// </summary>
    [ObservableProperty]
    [property: MaxLength(500, ErrorMessage = "事件订阅模式长度过长")]
    private string _notificationEventSubscribe = string.Empty;
    #endregion

    #region 全局通知设置
    /// <summary>
    /// 是否包含截图
    /// 提供丰富的上下文信息，便于问题追踪
    /// </summary>
    [ObservableProperty]
    private bool _includeScreenShot = true;
    #endregion

    #region Webhook通知通道
    /// <summary>
    /// Webhook通知配置
    /// 支持第三方系统集成的通知触发机制
    /// </summary>
    [ObservableProperty]
    private bool _webhookEnabled;

    [ObservableProperty]
    [property: Url(ErrorMessage = "Webhook终端点URL无效")]
    private string _webhookEndpoint = string.Empty;
    #endregion

    #region WebSocket通知通道
    /// <summary>
    /// WebSocket实时通知配置
    /// 支持低延迟双向通信
    /// </summary>
    [ObservableProperty]
    private bool _webSocketNotificationEnabled = false;

    [ObservableProperty]
    [property: Url(ErrorMessage = "WebSocket终端点URL无效")]
    private string _webSocketEndpoint = string.Empty;
    #endregion

    #region 平台特定通知通道
    /// <summary>
    /// Windows UWP原生通知支持
    /// 利用平台原生通知能力
    /// </summary>
    [ObservableProperty]
    private bool _windowsUwpNotificationEnabled = false;
    #endregion

    #region 企业通讯平台
    /// <summary>
    /// 飞书企业消息集成
    /// 支持企业级消息通知
    /// </summary>
    [ObservableProperty]
    private bool _feishuNotificationEnabled = false;

    [ObservableProperty]
    private string _feishuWebhookUrl = string.Empty;

    /// <summary>
    /// 企业微信消息集成
    /// 支持企业协作通知
    /// </summary>
    [ObservableProperty]
    private bool _workweixinNotificationEnabled = false;

    [ObservableProperty]
    private string _workweixinWebhookUrl = string.Empty;
    #endregion

    #region 电子邮件通知通道
    /// <summary>
    /// 电子邮件通知配置
    /// 提供全面的SMTP配置支持
    /// </summary>
    [ObservableProperty]
    private bool _emailNotificationEnabled = false;

    [ObservableProperty]
    [property: StringLength(255, MinimumLength = 1, ErrorMessage = "SMTP服务器名称必须在1-255字符之间")]
    private string _smtpServer = string.Empty;

    [ObservableProperty]
    [property: Range(1, 65535, ErrorMessage = "端口必须在1-65535范围内")]
    private int _smtpPort = 587; // 默认使用提交端口

    [ObservableProperty]
    [property: StringLength(100, ErrorMessage = "用户名过长")]
    private string _smtpUsername = string.Empty;

    [ObservableProperty]
    [property: StringLength(255, ErrorMessage = "密码过长")]
    private string _smtpPassword = string.Empty;

    [ObservableProperty]
    [property: EmailAddress(ErrorMessage = "发件人邮箱地址无效")]
    private string _fromEmail = string.Empty;

    [ObservableProperty]
    [property: StringLength(100, ErrorMessage = "发件人名称过长")]
    private string _fromName = string.Empty;

    [ObservableProperty]
    [property: EmailAddress(ErrorMessage = "收件人邮箱地址无效")]
    private string _toEmail = string.Empty;
    #endregion

    #region Bark移动推送通知
    /// <summary>
    /// Bark移动推送通知配置
    /// 支持现代移动推送通知策略
    /// </summary>
    [ObservableProperty]
    private bool _barkNotificationEnabled = false;

    [ObservableProperty]
    [property: Url(ErrorMessage = "Bark API终端点无效")]
    private string _barkApiEndpoint = "https://api.day.app/push";

    [ObservableProperty]
    private string[] _barkDeviceKeys = Array.Empty<string>();
    #endregion

    /// <summary>
    /// 配置全面验证方法
    /// 动态检查各通道配置并提供详细错误信息
    /// </summary>
    /// <returns>配置验证错误集合</returns>
    public IEnumerable<string> Validate()
    {
        var errors = new List<string>();

        // Webhook通道验证
        if (_webhookEnabled && string.IsNullOrWhiteSpace(_webhookEndpoint))
            errors.Add("Webhook已启用，但终端点为空");

        // 电子邮件通道验证
        if (_emailNotificationEnabled)
        {
            if (string.IsNullOrWhiteSpace(_smtpServer))
                errors.Add("电子邮件通知需要配置SMTP服务器");

            if (_smtpPort <= 0 || _smtpPort > 65535)
                errors.Add("SMTP端口无效");
        }

        // Bark通知通道验证
        if (_barkNotificationEnabled && (_barkDeviceKeys == null || _barkDeviceKeys.Length == 0))
            errors.Add("Bark通知已启用，但未提供设备密钥");

        return errors;
    }

    /// <summary>
    /// 重置所有通知配置到默认状态
    /// 提供配置的"清零"功能，支持快速重新配置
    /// </summary>
    public void ResetToDefaults()
    {
        _notificationEventSubscribe = string.Empty;
        _includeScreenShot = true;

        _webhookEnabled = false;
        _webhookEndpoint = string.Empty;

        _webSocketNotificationEnabled = false;
        _webSocketEndpoint = string.Empty;

        _windowsUwpNotificationEnabled = false;

        _feishuNotificationEnabled = false;
        _feishuWebhookUrl = string.Empty;

        _workweixinNotificationEnabled = false;
        _workweixinWebhookUrl = string.Empty;

        _emailNotificationEnabled = false;
        _smtpServer = string.Empty;
        _smtpPort = 587;
        _smtpUsername = string.Empty;
        _smtpPassword = string.Empty;
        _fromEmail = string.Empty;
        _fromName = string.Empty;
        _toEmail = string.Empty;

        _barkNotificationEnabled = false;
        _barkApiEndpoint = "https://api.day.app/push";
        _barkDeviceKeys = Array.Empty<string>();
    }
}
