using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class MacroSettingsPageViewModel : ViewModel
{
    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private string[] _quickFightMacroHotkeyMode = [OneKeyFightTask.HoldOnMode, OneKeyFightTask.TickMode];

    public MacroSettingsPageViewModel(IConfigService configService, INavigationService navigationService)
    {
        Config = configService.Get();
        _navigationService = navigationService;
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
        Process.Start(new ProcessStartInfo("https://bettergi.com/feats/macro/onem.html") { UseShellExecute = true });
    }
}
