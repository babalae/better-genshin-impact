using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public class TriggerSettingsPageViewModel : ObservableObject, INavigationAware
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
}