using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.System;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class NotificationSettingsPageViewModel : ObservableObject, IViewModel
{
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
    private async Task OnOpenNotificationEventDocument()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/dev/webhook.html#%E4%BA%8B%E4%BB%B6%E5%88%97%E8%A1%A8"));
    }
}
