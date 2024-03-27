using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class NotificationSettingsPageViewModel : ObservableObject
{
    public AllConfig Config { get; set; }

    private readonly NotificationManager _notificationManager;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private string _webhookStatus = "";

    public NotificationSettingsPageViewModel(IConfigService configService, NotificationManager notificationManager)
    {
        Config = configService.Get();
        _notificationManager = notificationManager;
    }

    [RelayCommand]
    private async Task OnTestWebhook()
    {
        IsLoading = true;
        WebhookStatus = "";

        var n = _notificationManager.GetNotifier<WebhookNotifier>();
        var res = await n.Notify(LifecycleNotificationData.Test());

        WebhookStatus = res.Message;

        IsLoading = false;
    }
}
