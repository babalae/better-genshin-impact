using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class OneDragonFlowConfig : ObservableObject
{
    
    // 配置名
    [ObservableProperty]
    private string _name = string.Empty;
    
    // 合成树脂的国家
    [ObservableProperty]
    private string _craftingBenchCountry = "枫丹";

    // 冒险者协会的国家
    [ObservableProperty]
    private string _adventurersGuildCountry = "枫丹";
    
    // 自动战斗配置的队伍名称
    [ObservableProperty]
    private string _partyName = string.Empty;

    // 自动战斗配置的策略名称
    [ObservableProperty]
    private string _domainName = string.Empty;

    // 每周秘境配置
    [ObservableProperty]
    private bool _weeklyDomainEnabled = false;

    [ObservableProperty]
    private string _mondayThursdayPartyName = string.Empty;

    [ObservableProperty]
    private string _mondayThursdayDomainName = string.Empty;

    [ObservableProperty]
    private string _tuesdayFridayPartyName = string.Empty;

    [ObservableProperty]
    private string _tuesdayFridayDomainName = string.Empty;

    [ObservableProperty]
    private string _wednesdaySaturdayPartyName = string.Empty;

    [ObservableProperty]
    private string _wednesdaySaturdayDomainName = string.Empty;

    [ObservableProperty]
    private string _sundayPartyName = string.Empty;

    [ObservableProperty]
    private string _sundayDomainName = string.Empty;
}