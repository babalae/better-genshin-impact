using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
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

    // 地脉花一条龙模式（跳过部分准备流程）
    [ObservableProperty]
    private bool _leyLineOneDragonMode = false;

    // 地脉花运行日期设置
    [ObservableProperty]
    private bool _leyLineRunMonday = true;

    [ObservableProperty]
    private bool _leyLineRunTuesday = true;

    [ObservableProperty]
    private bool _leyLineRunWednesday = true;

    [ObservableProperty]
    private bool _leyLineRunThursday = true;

    [ObservableProperty]
    private bool _leyLineRunFriday = true;

    [ObservableProperty]
    private bool _leyLineRunSaturday = true;

    [ObservableProperty]
    private bool _leyLineRunSunday = true;

    // 地脉花每日类型与国家配置（为空则使用独立任务配置）
    [ObservableProperty]
    private string _leyLineMondayType = string.Empty;

    [ObservableProperty]
    private string _leyLineMondayCountry = string.Empty;

    [ObservableProperty]
    private string _leyLineTuesdayType = string.Empty;

    [ObservableProperty]
    private string _leyLineTuesdayCountry = string.Empty;

    [ObservableProperty]
    private string _leyLineWednesdayType = string.Empty;

    [ObservableProperty]
    private string _leyLineWednesdayCountry = string.Empty;

    [ObservableProperty]
    private string _leyLineThursdayType = string.Empty;

    [ObservableProperty]
    private string _leyLineThursdayCountry = string.Empty;

    [ObservableProperty]
    private string _leyLineFridayType = string.Empty;

    [ObservableProperty]
    private string _leyLineFridayCountry = string.Empty;

    [ObservableProperty]
    private string _leyLineSaturdayType = string.Empty;

    [ObservableProperty]
    private string _leyLineSaturdayCountry = string.Empty;

    [ObservableProperty]
    private string _leyLineSundayType = string.Empty;

    [ObservableProperty]
    private string _leyLineSundayCountry = string.Empty;

    // 地脉花刷取次数（0 表示使用独立任务配置）
    [ObservableProperty]
    private int _leyLineRunCount = 0;

    // 地脉花树脂耗尽模式
    [ObservableProperty]
    private bool _leyLineResinExhaustionMode = false;

    // 地脉花刷取次数取小值（仅耗尽模式生效）
    [ObservableProperty]
    private bool _leyLineOpenModeCountMin = false;

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

    public bool ShouldRunLeyLineToday()
    {
        if (!LeyLineRunMonday
            && !LeyLineRunTuesday
            && !LeyLineRunWednesday
            && !LeyLineRunThursday
            && !LeyLineRunFriday
            && !LeyLineRunSaturday
            && !LeyLineRunSunday)
        {
            return true;
        }

        var serverTime = ServerTimeHelper.GetServerTimeNow();
        var dayOfWeek = (serverTime.Hour >= 4 ? serverTime : serverTime.AddDays(-1)).DayOfWeek;
        return dayOfWeek switch
        {
            DayOfWeek.Monday => LeyLineRunMonday,
            DayOfWeek.Tuesday => LeyLineRunTuesday,
            DayOfWeek.Wednesday => LeyLineRunWednesday,
            DayOfWeek.Thursday => LeyLineRunThursday,
            DayOfWeek.Friday => LeyLineRunFriday,
            DayOfWeek.Saturday => LeyLineRunSaturday,
            DayOfWeek.Sunday => LeyLineRunSunday,
            _ => true
        };
    }

    public (string type, string country) GetLeyLineConfigForToday(AutoLeyLineOutcropConfig fallback)
    {
        var serverTime = ServerTimeHelper.GetServerTimeNow();
        var dayOfWeek = (serverTime.Hour >= 4 ? serverTime : serverTime.AddDays(-1)).DayOfWeek;
        var (type, country) = dayOfWeek switch
        {
            DayOfWeek.Monday => (LeyLineMondayType, LeyLineMondayCountry),
            DayOfWeek.Tuesday => (LeyLineTuesdayType, LeyLineTuesdayCountry),
            DayOfWeek.Wednesday => (LeyLineWednesdayType, LeyLineWednesdayCountry),
            DayOfWeek.Thursday => (LeyLineThursdayType, LeyLineThursdayCountry),
            DayOfWeek.Friday => (LeyLineFridayType, LeyLineFridayCountry),
            DayOfWeek.Saturday => (LeyLineSaturdayType, LeyLineSaturdayCountry),
            DayOfWeek.Sunday => (LeyLineSundayType, LeyLineSundayCountry),
            _ => (string.Empty, string.Empty)
        };

        if (string.IsNullOrWhiteSpace(type))
        {
            type = fallback.LeyLineOutcropType;
        }

        if (string.IsNullOrWhiteSpace(country))
        {
            country = fallback.Country;
        }

        return (type, country);
    }

    #endregion
}
