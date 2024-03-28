using System.Diagnostics;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Wpf.Ui.Controls;
using BetterGenshinImpact.View.Pages;
using Wpf.Ui;
using System;
using System.IO;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class CommonSettingsPageViewModel : ObservableObject, INavigationAware, IViewModel
{
    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;

    public CommonSettingsPageViewModel(IConfigService configService, INavigationService navigationService)
    {
        Config = configService.Get();
        _navigationService = navigationService;
    }

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    public void OnRefreshMaskSettings()
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "RefreshSettings", new object(), "重新计算控件位置"));
    }

    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }

    [RelayCommand]
    public void OnSwitchTakenScreenshotEnabled()
    {
        if (Config.CommonConfig.ScreenshotEnabled)
        {
            if (TaskTriggerDispatcher.Instance().GetCacheCaptureMode() == DispatcherCaptureModeEnum.OnlyTrigger)
            {
                TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.CacheCaptureWithTrigger);
            }
        }
    }

    [RelayCommand]
    public void OnGoToFolder()
    {
        var path = Global.Absolute(@"log\screenshot\");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    public void OnOpenMapViewer()
    {
        new MapViewer().Show();
    }
}
