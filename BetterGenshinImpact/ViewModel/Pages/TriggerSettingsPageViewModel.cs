using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using Wpf.Ui.Controls;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class TriggerSettingsPageViewModel : ObservableObject, INavigationAware
{
    public AllConfig Config { get; set; }

    public TriggerSettingsPageViewModel(IConfigService configService)
    {
        Config = configService.Get();
    }

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {
    }


    [RelayCommand]
    private void OnEditBlacklist()
    {
        Process.Start("notepad.exe", Global.Absolute(@"Config\pick_black_lists.json"));
    }

    [RelayCommand]
    private void OnEditWhitelist()
    {
        Process.Start("notepad.exe", Global.Absolute(@"Config\pick_white_lists.json"));
    }
}