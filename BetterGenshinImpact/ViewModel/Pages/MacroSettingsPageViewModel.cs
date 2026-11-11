using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.QuickClaimReward;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class MacroSettingsPageViewModel : ViewModel
{
    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private string[] _quickFightMacroHotkeyMode = [OneKeyFightTask.HoldOnMode, OneKeyFightTask.HoldFinishMode, OneKeyFightTask.TickMode];

    [ObservableProperty]
    private string[] _oneKeyClaimRewardHotkeyMode = [OneKeyClaimRewardTask.ClickOnceMode, OneKeyClaimRewardTask.HoldMode];

    [ObservableProperty]
    private bool _oneKeyClaimRewardScrollOptionEnabled;

    [ObservableProperty]
    private bool _oneKeyClaimRewardScrollAmountOptionEnabled;

    public MacroSettingsPageViewModel(IConfigService configService, INavigationService navigationService)
    {
        Config = configService.Get();
        _navigationService = navigationService;
        Config.MacroConfig.PropertyChanged += OnMacroConfigPropertyChanged;
        UpdateOneKeyClaimRewardScrollOptionEnabled();
    }

    private void OnMacroConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MacroConfig.OneKeyClaimRewardHotkeyMode) ||
            e.PropertyName == nameof(MacroConfig.OneKeyClaimRewardScrollDownEnabled))
        {
            UpdateOneKeyClaimRewardScrollOptionEnabled();
        }
    }

    private void UpdateOneKeyClaimRewardScrollOptionEnabled()
    {
        OneKeyClaimRewardScrollOptionEnabled = Config.MacroConfig.OneKeyClaimRewardHotkeyMode == OneKeyClaimRewardTask.HoldMode;
        OneKeyClaimRewardScrollAmountOptionEnabled =
            OneKeyClaimRewardScrollOptionEnabled && Config.MacroConfig.OneKeyClaimRewardScrollDownEnabled;
    }
    
    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }

    /// <summary>
    /// 打开按键宏页面
    /// </summary>
    [RelayCommand]
    public void OnGoToKeyMacroPage()
    {
        _navigationService.Navigate(typeof(KeyMacroPage));
    }

    /// <summary>
    /// 打开一键宏配置窗口；配置文件无法解析时提示并改用 JSON 编辑器打开
    /// </summary>
    [RelayCommand]
    public void OnEditAvatarMacro()
    {
        AvatarMacroConfigWindow window;
        try
        {
            window = new AvatarMacroConfigWindow
            {
                Owner = Application.Current.MainWindow
            };
        }
        catch (Exception e)
        {
            ThemedMessageBox.Error(I18nService.Instance.Translate("读取一键宏配置失败，请在 JSON 编辑器中修正：") + e.Message);
            OnEditAvatarMacroJson();
            return;
        }

        window.ShowDialog();
    }

    /// <summary>
    /// 直接编辑一键宏 JSON 配置文件
    /// </summary>
    [RelayCommand]
    public void OnEditAvatarMacroJson()
    {
        JsonMonoDialog.Show(OneKeyFightTask.GetAvatarMacroJsonPath());
    }

    [RelayCommand]
    public void OnGoToOneKeyMacroUrl()
    {
        Process.Start(new ProcessStartInfo("https://www.bettergi.com/feats/macro/onem.html") { UseShellExecute = true });
    }
}
