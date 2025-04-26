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
    
    // 领取每日奖励的好感队伍名称
    [ObservableProperty]
    private string _dailyRewardPartyName = string.Empty;


    #region 每周秘境配置
    //=====================================================lcb
    //周一LCB
    [ObservableProperty]
    private string _mondayPartyName = string.Empty;
    
    [ObservableProperty]
    private string _mondayDomainName = string.Empty;
    
    
    //周二
    [ObservableProperty]
    private string _tuesdayPartyName = string.Empty;
    
    [ObservableProperty]
    private string _tuesdayDomainName = string.Empty;
    
    //周三
    [ObservableProperty]
    private string _wednesdayPartyName = string.Empty;
    
    [ObservableProperty]
    private string _wednesdayDomainName = string.Empty;
    
    //周四
    [ObservableProperty]
    private string _thursdayPartyName = string.Empty;
    
    [ObservableProperty]
    private string _thursdayDomainName = string.Empty;
    
    //周五
    [ObservableProperty]
    private string _fridayPartyName = string.Empty;
    
    [ObservableProperty]
    private string _fridayDomainName = string.Empty;
    
    //周六
    [ObservableProperty]
    private string _saturdayPartyName = string.Empty;
    
    [ObservableProperty]
    private string _saturdayDomainName = string.Empty;
    
    //周日
    [ObservableProperty]
    private string _sundayPartyName = string.Empty;

    [ObservableProperty]
    private string _sundayDomainName = string.Empty;

    // 完成后操作
    [ObservableProperty]
    private string _completionAction = string.Empty;
    
    // 通过当天是哪一天来返回配置
    public (string partyName, string domainName) GetDomainConfig()
    {
        if (WeeklyDomainEnabled)
        {
            var dayOfWeek = DateTime.Now.DayOfWeek;
            return dayOfWeek switch
            {
                DayOfWeek.Monday => (MondayPartyName, MondayDomainName),
                DayOfWeek.Tuesday => (TuesdayPartyName, TuesdayDomainName),
                DayOfWeek.Wednesday => (WednesdayPartyName, WednesdayDomainName),
                DayOfWeek.Thursday => (ThursdayPartyName, ThursdayDomainName),
                DayOfWeek.Friday => (FridayPartyName, FridayDomainName),
                DayOfWeek.Saturday => (SaturdayPartyName, SaturdayDomainName),
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