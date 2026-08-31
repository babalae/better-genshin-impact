using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Windows.System;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class NotificationSettingsPageViewModel : ObservableObject, IViewModel
{
    private static readonly Microsoft.Extensions.Logging.ILogger<NotificationSettingsPageViewModel> Logger =
        App.GetLogger<NotificationSettingsPageViewModel>();

    private readonly NotificationService _notificationService;
    private readonly HashSet<string> _knownNotificationEventCodes;
    private bool _isSyncingNotificationEventSelection;

    [ObservableProperty] private string _barkStatus = string.Empty;

    /// <summary>
    ///     钉钉通知测试状态
    /// </summary>
    [ObservableProperty] private string _dingDingStatus = string.Empty;

    [ObservableProperty] private string _emailStatus = string.Empty;

    [ObservableProperty] private string _feishuStatus = string.Empty;

    [ObservableProperty] private string _oneBotStatus = string.Empty;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private ObservableCollection<NotificationEventOption> _notificationEventOptions = [];

    [ObservableProperty] private string _notificationEventSelectionSummary = string.Empty;

    [ObservableProperty] private string _telegramStatus = string.Empty;

    [ObservableProperty] private string _webhookStatus = string.Empty;

    [ObservableProperty] private string _webSocketStatus = string.Empty;

    [ObservableProperty] private string _windowsUwpStatus = string.Empty;

    [ObservableProperty] private string _workWeixinStatus = string.Empty;

    [ObservableProperty] private string _xxtuiStatus = string.Empty;

    [ObservableProperty] private string _discordStatus = string.Empty;

    [ObservableProperty] private string[] _discordImageEncoderNames =
    [
        nameof(DiscordWebhookNotifier.ImageEncoderEnum.Png),
        nameof(DiscordWebhookNotifier.ImageEncoderEnum.Jpeg),
        nameof(DiscordWebhookNotifier.ImageEncoderEnum.WebP)
    ];

    [ObservableProperty] private string _serverChanStatus = string.Empty;

    [ObservableProperty] private string _meowStatus = string.Empty;

    [ObservableProperty] private string _gotifyStatus = string.Empty;

    [ObservableProperty] private string _qqStatus = string.Empty;

    [ObservableProperty] private string _wechatClawbotStatus = string.Empty;

    /// <summary>
    /// 是否正在执行绑定流程（控制按钮显示为"绑定"或"取消"）
    /// </summary>
    [ObservableProperty] private bool _isBinding;

    /// <summary>
    /// 是否正在执行微信 Clawbot 登录/绑定流程
    /// </summary>
    [ObservableProperty] private bool _isWechatClawbotBinding;

    /// <summary>
    /// 是否正在执行群 QQ 绑定流程
    /// </summary>
    [ObservableProperty] private bool _isBindingGroup;

    /// <summary>
    /// 绑定流程的取消令牌源，用于用户点击取消时中断 WebSocket 连接
    /// </summary>
    private CancellationTokenSource? _bindCts;

    /// <summary>
    /// 微信 Clawbot 登录/绑定流程的取消令牌源
    /// </summary>
    private CancellationTokenSource? _wechatClawbotBindCts;

    /// <summary>
    /// 群 QQ 绑定流程的取消令牌源
    /// </summary>
    private CancellationTokenSource? _groupBindCts;

    /// <summary>
    /// 构造通知设置页 ViewModel，并订阅微信 Clawbot 推送会话过期事件，
    /// 使普通发送失败时也能在设置页状态文本上提示用户如何重新激活推送端口。
    /// </summary>
    public NotificationSettingsPageViewModel(IConfigService configService, NotificationService notificationService)
    {
        Config = configService.Get();
        _notificationService = notificationService;

        _knownNotificationEventCodes = NotificationEvent
            .GetAll()
            .Select(notificationEvent => notificationEvent.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        NotificationEventOptions = new ObservableCollection<NotificationEventOption>(
            NotificationEvent
                .GetAll()
                .Select(notificationEvent => new NotificationEventOption(notificationEvent.Code, notificationEvent.Msg)));

        foreach (var option in NotificationEventOptions)
        {
            option.PropertyChanged += OnNotificationEventOptionPropertyChanged;
        }

        Config.NotificationConfig.PropertyChanged += OnNotificationConfigPropertyChanged;
        ApplyNotificationEventSelectionFromConfig();

        // 订阅微信 Clawbot 推送会话过期事件：普通发送（非测试按钮）失败时，
        // 也能及时在设置页状态文本上提示用户如何重新激活推送端口。
        WechatClawbotNotifier.SessionExpired += OnWechatClawbotSessionExpired;
    }

    /// <summary>
    /// 微信 Clawbot 推送会话过期回调（后台线程触发），编组回 UI 线程更新状态提示。
    /// </summary>
    private void OnWechatClawbotSessionExpired(string hint)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => WechatClawbotStatus = hint);
    }

    public AllConfig Config { get; set; }

    [RelayCommand]
    private void SelectAllNotificationEvents()
    {
        SetNotificationEventSelection(true);
    }

    [RelayCommand]
    private void ClearNotificationEventSelection()
    {
        SetNotificationEventSelection(false);
    }

    private void SetNotificationEventSelection(bool isSelected)
    {
        _isSyncingNotificationEventSelection = true;
        try
        {
            foreach (var option in NotificationEventOptions)
            {
                option.IsSelected = isSelected;
            }
        }
        finally
        {
            _isSyncingNotificationEventSelection = false;
        }

        UpdateNotificationEventSubscribeFromSelection();
    }

    private void OnNotificationConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NotificationConfig.NotificationEventSubscribe))
        {
            return;
        }

        if (_isSyncingNotificationEventSelection)
        {
            return;
        }

        ApplyNotificationEventSelectionFromConfig();
    }

    private void OnNotificationEventOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NotificationEventOption.IsSelected))
        {
            return;
        }

        if (_isSyncingNotificationEventSelection)
        {
            return;
        }

        UpdateNotificationEventSubscribeFromSelection();
    }

    private void ApplyNotificationEventSelectionFromConfig()
    {
        var parsedEventCodes = NotificationEventSubscriptionHelper.ParseEventCodes(
            Config.NotificationConfig.NotificationEventSubscribe);
        var selectedEventCodes = parsedEventCodes
            .Where(eventCode => _knownNotificationEventCodes.Contains(eventCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownEventCodeCount = parsedEventCodes.Count - selectedEventCodes.Count;

        _isSyncingNotificationEventSelection = true;
        try
        {
            foreach (var option in NotificationEventOptions)
            {
                option.IsSelected = selectedEventCodes.Contains(option.Code);
            }
        }
        finally
        {
            _isSyncingNotificationEventSelection = false;
        }

        UpdateNotificationEventSelectionSummary(unknownEventCodeCount);
    }

    private void UpdateNotificationEventSubscribeFromSelection()
    {
        var normalizedEventCodes = NotificationEventSubscriptionHelper.NormalizeEventCodes(
            NotificationEventOptions
                .Where(option => option.IsSelected)
                .Select(option => option.Code));

        UpdateNotificationEventSelectionSummary();

        if (string.Equals(
                Config.NotificationConfig.NotificationEventSubscribe,
                normalizedEventCodes,
                StringComparison.Ordinal))
        {
            return;
        }

        Config.NotificationConfig.NotificationEventSubscribe = normalizedEventCodes;
    }

    private void UpdateNotificationEventSelectionSummary(int unknownEventCodeCount = 0)
    {
        if (NotificationEventOptions.Count == 0)
        {
            NotificationEventSelectionSummary = "当前版本没有可配置的通知事件";
            return;
        }

        var selectedCount = NotificationEventOptions.Count(option => option.IsSelected);
        if (unknownEventCodeCount > 0)
        {
            NotificationEventSelectionSummary = selectedCount == 0
                ? $"检测到 {unknownEventCodeCount} 个未知事件代码，当前未显示；修改后会自动清理。未勾选任何事件时按“全部通知”处理"
                : $"已选择 {selectedCount} / {NotificationEventOptions.Count} 个事件；另有 {unknownEventCodeCount} 个未知事件代码，修改后会自动清理";
            return;
        }

        NotificationEventSelectionSummary = selectedCount == 0
            ? "当前未勾选任何事件，将按“全部通知”处理"
            : $"已选择 {selectedCount} / {NotificationEventOptions.Count} 个事件";
    }

    [RelayCommand]
    private async Task OnTestWebhook()
    {
        IsLoading = true;
        WebhookStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<WebhookNotifier>();

        WebhookStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestWindowsUwpNotification()
    {
        IsLoading = true;
        WindowsUwpStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<WindowsUwpNotifier>();

        WindowsUwpStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestFeishuNotification()
    {
        IsLoading = true;
        FeishuStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<FeishuNotifier>();

        FeishuStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestOneBotNotification()
    {
        IsLoading = true;
        OneBotStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<OneBotNotifier>();

        OneBotStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestWorkWeixinNotification()
    {
        IsLoading = true;
        WorkWeixinStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<WorkWeixinNotifier>();

        WorkWeixinStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestWebSocketNotification()
    {
        IsLoading = true;
        WebSocketStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<WebSocketNotifier>();

        WebSocketStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestEmailNotification()
    {
        IsLoading = true;
        EmailStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<EmailNotifier>();

        EmailStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestBarkNotification()
    {
        IsLoading = true;
        BarkStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<BarkNotifier>();

        BarkStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestTelegramNotification()
    {
        IsLoading = true;
        TelegramStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<TelegramNotifier>();

        TelegramStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestXxtuiNotification()
    {
        IsLoading = true;
        XxtuiStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<XxtuiNotifier>();

        XxtuiStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestDingDingWebhookNotification()
    {
        IsLoading = true;
        DingDingStatus = string.Empty; // 使用专门的状态变量，与xxtui保持一致

        var res = await _notificationService.TestNotifierAsync<DingDingWebhook>();

        DingDingStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestDiscordWebhookNotification()
    {
        IsLoading = true;
        DiscordStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<DiscordWebhookNotifier>();

        DiscordStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestServerChanNotification()
    {
        IsLoading = true;
        ServerChanStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<ServerChanNotifier>();

        ServerChanStatus = res.Message;

        // 添加Toast提示
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestMeowNotification()
    {
        IsLoading = true;
        MeowStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<MeowNotifier>();

        MeowStatus = res.Message;

        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestGotifyNotification()
    {
        IsLoading = true;
        GotifyStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<GotifyNotifier>();

        GotifyStatus = res.Message;
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestQqNotification()
    {
        IsLoading = true;
        QqStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<QqNotifier>();

        QqStatus = res.Message;
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task OnTestWechatClawbotNotification()
    {
        IsLoading = true;
        WechatClawbotStatus = string.Empty;

        var res = await _notificationService.TestNotifierAsync<WechatClawbotNotifier>();

        WechatClawbotStatus = res.Message;
        if (res.IsSuccess)
            Toast.Success(res.Message);
        else
            Toast.Error(res.Message);

        IsLoading = false;
    }

    /// <summary>
    /// 绑定 QQ 按钮。连接 QQ 网关，等待用户发送验证码，自动回填 OpenID。
    /// 支持取消（用户点击取消时中断 WebSocket 连接）。
    /// 超时（60 秒未收到消息）时提示用户重试。
    /// </summary>
    [RelayCommand]
    private async Task OnBindQq()
    {
        if (string.IsNullOrWhiteSpace(Config.NotificationConfig.QqAppId))
        {
            Toast.Error("请先填写 QQ AppID");
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.NotificationConfig.QqClientSecret))
        {
            Toast.Error("请先填写 QQ AppSecret");
            return;
        }

        IsBinding = true;
        QqStatus = "正在连接 QQ 网关…";
        _bindCts = new CancellationTokenSource();

        try
        {
            var openId = await QqWebSocketHelper.BindAsync(
                Config.NotificationConfig.QqAppId,
                Config.NotificationConfig.QqClientSecret,
                code =>
                {
                    // 回调在后台线程执行，需要编组回 UI 线程才能更新 ObservableProperty
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        QqStatus = $"请用手机 QQ 私聊机器人，发送验证码 [{code}]";
                    });
                },
                _bindCts.Token);

            // 绑定成功，自动回填 OpenID（配置自动保存 + 刷新通知器）
            Config.NotificationConfig.QqOpenId = openId;
            QqStatus = "绑定成功";
            Toast.Success("QQ 绑定成功");
        }
        catch (OperationCanceledException)
        {
            // 用户主动点击取消
            QqStatus = "已取消绑定";
        }
        catch (System.Exception ex)
        {
            // 超时或其他错误（超时已被 BindAsync 转为 NotifierException）
            QqStatus = $"绑定失败：{ex.Message}";
            Toast.Error($"绑定失败：{ex.Message}");
        }
        finally
        {
            IsBinding = false;
            _bindCts.Dispose();
            _bindCts = null;
        }
    }

    /// <summary>
    /// 取消绑定按钮：取消 WebSocket 连接，中断绑定流程。
    /// </summary>
    [RelayCommand]
    private void OnCancelBindQq()
    {
        _bindCts?.Cancel();
    }

    /// <summary>
    /// 微信 Clawbot 登录并绑定按钮。扫码登录获取 bot_token，再等待用户发送一次性验证码，
    /// 自动回填 to_user_id 和 context_token。支持取消。
    /// </summary>
    [RelayCommand]
    private async Task OnBindWechatClawbot()
    {
        IsWechatClawbotBinding = true;
        WechatClawbotStatus = "正在获取登录二维码…";
        _wechatClawbotBindCts = new CancellationTokenSource();

        try
        {
            // 先完成整个登录 + 绑定流程，成功后一次性提交全部凭证，
            // 避免中途取消/失败时遗留“新 BotToken + 旧 ToUserId”的混合状态。
            var login = await WechatClawbotHelper.LoginAsync(
                qrCodeUrl =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        WechatClawbotStatus = "请用手机微信扫码登录 Clawbot";
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(qrCodeUrl) { UseShellExecute = true });
                        }
                        catch (System.Exception ex)
                        {
                            WechatClawbotStatus = $"请用手机微信扫码登录 Clawbot（二维码链接：{qrCodeUrl}）";
                            Logger.LogWarning("打开微信 Clawbot 二维码链接失败: {Ex}", ex.Message);
                        }
                    });
                },
                _wechatClawbotBindCts.Token);

            WechatClawbotStatus = "登录成功，正在等待绑定消息…";

            var bind = await WechatClawbotHelper.BindAsync(
                login.BotToken,
                login.BaseUrl,
                login.UserId,
                code =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        WechatClawbotStatus = $"请给 Clawbot 发送验证码 [{code}] 完成绑定";
                    });
                },
                _wechatClawbotBindCts.Token);

            // 把会话令牌/游标存入独立存储（不触发全局配置刷新）
            await WechatClawbotSessionStore.SaveAsync(login.BotToken, bind.ContextToken, bind.GetUpdatesBuf);

            // 全部成功后在抑制刷新的作用域内一次性提交绑定凭证
            using (_notificationService.SuppressRefreshNotifiers())
            {
                Config.NotificationConfig.WechatClawbotBotToken = login.BotToken;
                Config.NotificationConfig.WechatClawbotBaseUrl = login.BaseUrl;
                Config.NotificationConfig.WechatClawbotToUserId = bind.ToUserId;
            }

            // 无需再显式刷新——SuppressRefreshNotifiers() 退出时已无条件调用 RefreshNotifiers()，
            // 确保通知器从独立存储重新加载最新 context_token。
            // 此处不再重复调用，避免第二次刷新销毁刚创建的会话导致 UI 卡死。

            WechatClawbotStatus = "绑定成功";
            Toast.Success("微信 Clawbot 绑定成功");
        }
        catch (OperationCanceledException)
        {
            WechatClawbotStatus = "已取消登录/绑定";
        }
        catch (System.Exception ex)
        {
            WechatClawbotStatus = $"登录/绑定失败：{ex.Message}";
            Toast.Error($"登录/绑定失败：{ex.Message}");
        }
        finally
        {
            IsWechatClawbotBinding = false;
            _wechatClawbotBindCts.Dispose();
            _wechatClawbotBindCts = null;
        }
    }

    /// <summary>
    /// 取消微信 Clawbot 登录/绑定按钮。
    /// </summary>
    [RelayCommand]
    private void OnCancelBindWechatClawbot()
    {
        _wechatClawbotBindCts?.Cancel();
    }

    /// <summary>
    /// 绑定 QQ 群按钮。连接 QQ 网关，等待用户将机器人加入群聊或发送验证码，自动回填群 OpenID。
    /// 支持取消（用户点击取消时中断 WebSocket 连接）。
    /// 超时（60 秒）时提示用户重试。
    /// </summary>
    [RelayCommand]
    private async Task OnBindGroupQq()
    {
        if (string.IsNullOrWhiteSpace(Config.NotificationConfig.QqAppId))
        {
            Toast.Error("请先填写 QQ AppID");
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.NotificationConfig.QqClientSecret))
        {
            Toast.Error("请先填写 QQ AppSecret");
            return;
        }

        IsBindingGroup = true;
        QqStatus = "正在连接 QQ 网关…";
        _groupBindCts = new CancellationTokenSource();

        try
        {
            var groupOpenId = await QqWebSocketHelper.BindGroupAsync(
                Config.NotificationConfig.QqAppId,
                Config.NotificationConfig.QqClientSecret,
                code =>
                {
                    // 回调在后台线程执行，需要编组回 UI 线程才能更新 ObservableProperty
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        QqStatus = $"请将机器人加入群聊，或在群里 @机器人 发送验证码 [{code}]";
                    });
                },
                status =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        QqStatus = status;
                    });
                },
                _groupBindCts.Token);

            // 绑定成功，自动回填群 OpenID（配置自动保存 + 刷新通知器）
            Config.NotificationConfig.QqGroupOpenId = groupOpenId;
            QqStatus = "群绑定成功";
            Toast.Success("QQ 群绑定成功");
        }
        catch (OperationCanceledException)
        {
            // 用户主动点击取消
            QqStatus = "已取消群绑定";
        }
        catch (System.Exception ex)
        {
            // 超时或其他错误（超时已被 BindGroupAsync 转为 NotifierException）
            QqStatus = $"群绑定失败：{ex.Message}";
            Toast.Error($"群绑定失败：{ex.Message}");
        }
        finally
        {
            IsBindingGroup = false;
            _groupBindCts?.Dispose();
            _groupBindCts = null;
        }
    }

    /// <summary>
    /// 取消群 QQ 绑定按钮：取消 WebSocket 连接，中断绑定流程。
    /// </summary>
    [RelayCommand]
    private void OnCancelBindGroupQq()
    {
        _groupBindCts?.Cancel();
    }

    [RelayCommand]
    private async Task OnOpenNotificationEventDocument()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/dev/webhook.html#%E4%BA%8B%E4%BB%B6%E5%88%97%E8%A1%A8"));
    }
}
