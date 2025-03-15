using System.Collections.ObjectModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.View;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class CommonSettingsPageViewModel : ViewModel
{
    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;

    private readonly NotificationService _notificationService;
    public ObservableCollection<string> CountryList { get; } = new();
    public ObservableCollection<string> Areas { get; } = new();
    private readonly TpConfig _tpConfig = TaskContext.Instance().Config.TpConfig;


    private string _selectedCountry = string.Empty;
    public string SelectedCountry
    {
        get => _selectedCountry;
        set
        {
            if (SetProperty(ref _selectedCountry, value))
            {
                UpdateAreas(value);
                SelectedArea = Areas.FirstOrDefault() ?? string.Empty;
            }
        }
    }

    private string _selectedArea = string.Empty;
    public string SelectedArea
    {
        get => _selectedArea;
        set
        {
            if (SetProperty(ref _selectedArea, value))
            {
                UpdateRevivePoint(SelectedCountry, SelectedArea);
            }
        }
    }

    private void InitializeCountries()
    {
        var countries = MapLazyAssets.Instance.GoddessPositions.Values
            .OrderBy(g => int.TryParse(g.Id, out int id) ? id : int.MaxValue) 
            .GroupBy(g => g.Country)   
            .Select(grp => grp.Key);    
        CountryList.Clear();
        foreach (var country in countries)
        {
            if (!string.IsNullOrEmpty(country))
            {
                CountryList.Add(country);
            }
        }
        _selectedCountry = _tpConfig.ReviveStatueOfTheSevenCountry;
        UpdateAreas(SelectedCountry);
        _selectedArea = _tpConfig.ReviveStatueOfTheSevenArea;
        UpdateRevivePoint(SelectedCountry, SelectedArea);
    }

    private void UpdateAreas(string country)
    {
        Areas.Clear();
        SelectedArea = string.Empty; 
        if (string.IsNullOrEmpty(country)) return;
    
        var areas = MapLazyAssets.Instance.GoddessPositions.Values
            .Where(g => g.Country == country)
            .OrderBy(g => int.TryParse(g.Id, out int id) ? id : int.MaxValue) 
            .GroupBy(g => g.Area)       
            .Select(grp => grp.Key); 
        foreach (var area in areas)
        {
            if (!string.IsNullOrEmpty(area))
            {
                Areas.Add(area);
            }
        }
    }

   // 当国家或区域改变时更新坐标
    private void UpdateRevivePoint(string country, string area)
    {
        if (string.IsNullOrEmpty(country) || string.IsNullOrEmpty(area)) return;
    
        var goddess = MapLazyAssets.Instance.GoddessPositions.Values
            .FirstOrDefault(g => g.Country == country && g.Area == area);
        if (goddess == null) return;
        _tpConfig.ReviveStatueOfTheSevenCountry = country;
        _tpConfig.ReviveStatueOfTheSevenArea = area;
        _tpConfig.ReviveStatueOfTheSevenPointX = goddess.X;
        _tpConfig.ReviveStatueOfTheSevenPointY = goddess.Y;
        _tpConfig.ReviveStatueOfTheSeven = goddess;
    }
    
    public CommonSettingsPageViewModel(IConfigService configService, INavigationService navigationService, NotificationService notificationService)
    {
        Config = configService.Get();
        _navigationService = navigationService;
        _notificationService = notificationService;
        InitializeCountries();
    }

    [RelayCommand]
    public void OnRefreshMaskSettings()
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "RefreshSettings", new object(), "重新计算控件位置"));
    }

    [RelayCommand]
    private void OnSwitchMaskEnabled()
    {
        // if (Config.MaskWindowConfig.MaskEnabled)
        // {
        //     MaskWindow.Instance().Show();
        // }
        // else
        // {
        //     MaskWindow.Instance().Hide();
        // }
    }

    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }

    [RelayCommand]
    public void OnSwitchTakenScreenshotEnabled()
    {
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
    public void OnGoToLogFolder()
    {
        var path = Global.Absolute(@"log");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    private async Task OnTestWebhook()
    {
        var res = await _notificationService.TestNotifierAsync<WebhookNotifier>();
        if(res.IsSuccess)
        {
            Toast.Success(res.Message);
        }
        else
        {
            Toast.Error(res.Message);
        }
    }

    [RelayCommand]
    private async Task OnTestWindowsUwpNotification()
    {
        var res = await _notificationService.TestNotifierAsync<WindowsUwpNotifier>();
        if(res.IsSuccess)
        {
            Toast.Success(res.Message);
        }
        else
        {
            Toast.Error(res.Message);
        }
    }
    
    [RelayCommand]
    private async Task OnTestFeishuNotification()
    {
        var res = await _notificationService.TestNotifierAsync<FeishuNotifier>();
        if(res.IsSuccess)
        {
            Toast.Success(res.Message);
        }
        else
        {
            Toast.Error(res.Message);
        }
    }
    
    [RelayCommand]
    private async Task OnTestWorkWeixinNotification()
    {
        var res = await _notificationService.TestNotifierAsync<WorkWeixinNotifier>();
        if(res.IsSuccess)
        {
            Toast.Success(res.Message);
        }
        else
        {
            Toast.Error(res.Message);
        }
    }
    
    [RelayCommand]
    private async Task OnTestWebSocketNotification()
    {
        var res = await _notificationService.TestNotifierAsync<WebSocketNotifier>();
        if(res.IsSuccess)
        {
            Toast.Success(res.Message);
        }
        else
        {
            Toast.Error(res.Message);
        }
    }
    
    [RelayCommand]
    private async Task OnTestEmailNotification()
    {
        var res = await _notificationService.TestNotifierAsync<EmailNotifier>();
        if(res.IsSuccess)
        {
            Toast.Success(res.Message);
        }
        else
        {
            Toast.Error(res.Message);
        }
    }
    
    [RelayCommand]
    private async Task OnTestBarkNotification()
    {
        var res = await _notificationService.TestNotifierAsync<BarkNotifier>();
        if(res.IsSuccess)
        {
            Toast.Success(res.Message);
        }
        else
        {
            Toast.Error(res.Message);
        }
    }
    
    [RelayCommand]
    private async Task OnTestTelegramNotification()
    {
        var res = await _notificationService.TestNotifierAsync<TelegramNotifier>();
        if(res.IsSuccess)
        {
            Toast.Success(res.Message);
        }
        else
        {
            Toast.Error(res.Message);
        }
    }
    
    
    [RelayCommand]
    private void ImportLocalScriptsRepoZip()
    {
        Directory.CreateDirectory(ScriptRepoUpdater.ReposPath);

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Zip Files (*.zip)|*.zip",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            var zipPath = dialog.FileName;
            // 删除旧文件夹
            if (Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
            {
                DirectoryHelper.DeleteReadOnlyDirectory(ScriptRepoUpdater.CenterRepoPath);
            }
            ZipFile.ExtractToDirectory(zipPath, ScriptRepoUpdater.ReposPath, true);
            
            if (Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
            {
                MessageBox.Information("脚本仓库离线包导入成功！");
            }
            else
            {
                MessageBox.Error("脚本仓库离线包导入失败，不正确的脚本仓库离线包内容！");
                DirectoryHelper.DeleteReadOnlyDirectory(ScriptRepoUpdater.ReposPath);
            }
        }
    }
}