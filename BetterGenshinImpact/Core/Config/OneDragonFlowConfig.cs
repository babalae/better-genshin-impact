using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
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
    
    // 合成浓缩后保留原粹树脂的数量
    [ObservableProperty]
    private int _minResinToKeep = 0;
    
    // 领取每日奖励的好感数量
    [ObservableProperty]
    private string _sundayEverySelectedValue = "0";
    
    // 领取每日奖励的好感数量
    [ObservableProperty]
    private string _sundaySelectedValue = "0";
    
    // 尘歌壶传送方式，1. 地图传送 2. 尘歌壶道具
    [ObservableProperty]
    private string _sereniteaPotTpType = "地图传送";
    
    // 尘歌壶洞天购买商品
    [ObservableProperty]
    private List<string> _secretTreasureObjects = new();

    #region 每周秘境配置

    //周一
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
    
    // 通过当天（4点起始）是哪一天来返回配置
    public (string partyName, string domainName, string sundaySelectedValue) GetDomainConfig()
    {
        if (WeeklyDomainEnabled)
        {
            var serverTime = ServerTimeHelper.GetServerTimeNow();
            var dayOfWeek = (serverTime.Hour >= 4 ? serverTime : serverTime.AddDays(-1)).DayOfWeek;
            return dayOfWeek switch
            {
                DayOfWeek.Monday => (MondayPartyName, MondayDomainName,SundaySelectedValue),
                DayOfWeek.Tuesday => (TuesdayPartyName, TuesdayDomainName,SundaySelectedValue),
                DayOfWeek.Wednesday => (WednesdayPartyName, WednesdayDomainName,SundaySelectedValue),
                DayOfWeek.Thursday => (ThursdayPartyName, ThursdayDomainName,SundaySelectedValue),
                DayOfWeek.Friday => (FridayPartyName, FridayDomainName,SundaySelectedValue),
                DayOfWeek.Saturday => (SaturdayPartyName, SaturdayDomainName,SundaySelectedValue),
                DayOfWeek.Sunday => (SundayPartyName, SundayDomainName,SundaySelectedValue),
                _ => (PartyName, DomainName,SundaySelectedValue)
            };
        }
        else
        {
            return (PartyName, DomainName,SundayEverySelectedValue);
        }
    }

    #endregion
}