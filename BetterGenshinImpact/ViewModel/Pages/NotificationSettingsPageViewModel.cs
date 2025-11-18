using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notifier;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class NotificationSettingsPageViewModel : ObservableObject, IViewModel
{
    private readonly NotificationService _notificationService;

    [ObservableProperty] private string _barkStatus = string.Empty;

    /// <summary>
    ///     钉钉通知测试状态
    /// </summary>
    [ObservableProperty] private string _dingDingStatus = string.Empty;

    [ObservableProperty] private string _emailStatus = string.Empty;

    [ObservableProperty] private string _feishuStatus = string.Empty;

    [ObservableProperty] private string _oneBotStatus = string.Empty;

    [ObservableProperty] private bool _isLoading;

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
    }

    public AllConfig Config { get; set; }

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
}