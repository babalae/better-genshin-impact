using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.QuickClaimReward;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Diagnostics;
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

    [RelayCommand]
    public void OnEditAvatarMacro()
    {
        JsonMonoDialog.Show(OneKeyFightTask.GetAvatarMacroJsonPath());
    }

    [RelayCommand]
    public void OnGoToOneKeyMacroUrl()
    {
        Process.Start(new ProcessStartInfo("https://www.bettergi.com/feats/macro/onem.html") { UseShellExecute = true });
    }
}
