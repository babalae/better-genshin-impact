using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
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

    [ObservableProperty]
    private List<KeyValuePair<string, string>> _inputModeOptions =
    [
        new(HardwareInputConfigValues.Virtual, "虛擬信號"),
        new(HardwareInputConfigValues.Hardware, "硬體"),
    ];

    [ObservableProperty]
    private List<KeyValuePair<string, string>> _hardwareVendorOptions =
    [
        new(HardwareInputConfigValues.Ferrum, "Ferrum"),
        new(HardwareInputConfigValues.Makcu, "Makcu"),
    ];

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
    public void RefreshHardwarePorts()
    {
        Config.HardwareInputConfig.RefreshDetectedPorts();
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
