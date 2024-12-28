using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class OneDragonFlowConfig : ObservableObject
{
    // 配置名
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 所有任务的开关状态
    /// </summary>
    public Dictionary<string, bool> TaskEnabledList { get; set; } = new();

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

    [ObservableProperty]
    private bool _weeklyDomainEnabled = false;

    #region 每周秘境配置

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

    // 通过当天是哪一天来返回配置
    public (string partyName, string domainName) GetDomainConfig()
    {
        if (WeeklyDomainEnabled)
        {
            var dayOfWeek = DateTime.Now.DayOfWeek;
            return dayOfWeek switch
            {
                DayOfWeek.Monday => (MondayThursdayPartyName, MondayThursdayDomainName),
                DayOfWeek.Tuesday => (TuesdayFridayPartyName, TuesdayFridayDomainName),
                DayOfWeek.Wednesday => (WednesdaySaturdayPartyName, WednesdaySaturdayDomainName),
                DayOfWeek.Thursday => (MondayThursdayPartyName, MondayThursdayDomainName),
                DayOfWeek.Friday => (TuesdayFridayPartyName, TuesdayFridayDomainName),
                DayOfWeek.Saturday => (WednesdaySaturdayPartyName, WednesdaySaturdayDomainName),
                DayOfWeek.Sunday => (SundayPartyName, SundayDomainName),
                _ => (PartyName, DomainName)
            };
        }
        else
        {
            return (PartyName, DomainName);
        }
    }

    #endregion
}