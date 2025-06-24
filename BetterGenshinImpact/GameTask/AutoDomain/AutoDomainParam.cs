using System.Collections.Generic;
using BetterGenshinImpact.GameTask.Model;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainParam : BaseTaskParam
{
    public int DomainRoundNum { get; set; }

    public string CombatStrategyPath { get; set; }

    // 刷副本使用的队伍名称
    public string PartyName { get; set; } = string.Empty;

    // 需要刷取的副本名称
    public string DomainName { get; set; } = string.Empty;
    
    // 需要刷取的副本名称
    public string SundaySelectedValue { get; set; } = string.Empty;

    // 结束后是否自动分解圣遗物
    public bool AutoArtifactSalvage { get; set; } = false;

    // 分解圣遗物的最大星级
    // 1~4
    public string MaxArtifactStar { get; set; } = "4";

    public bool SpecifyResinUse { get; set; } = false;
    
    // 使用树脂优先级
    public List<string> ResinPriorityList { get; set; } = 
    [
        "浓缩树脂",
        "原粹树脂"
    ];

    // 使用原粹树脂刷取副本次数
    public int OriginalResinUseCount { get; set; } = 0;

    // 使用浓缩树脂刷取副本次数
    public int CondensedResinUseCount { get; set; } = 0;

    // 使用须臾树脂刷取副本次数
    public int TransientResinUseCount { get; set; } = 0;

    // 使用脆弱树脂刷取副本次数
    public int FragileResinUseCount { get; set; } = 0;

    public AutoDomainParam(int domainRoundNum, string path)
    {
        DomainRoundNum = domainRoundNum;
        if (domainRoundNum == 0)
        {
            DomainRoundNum = 9999;
        }

        CombatStrategyPath = path;
        SetDefault();
    }

    public void SetDefault()
    {
        var config = TaskContext.Instance().Config.AutoDomainConfig;
        PartyName = config.PartyName;
        DomainName = config.DomainName;
        SundaySelectedValue = config.SundaySelectedValue;
        AutoArtifactSalvage = config.AutoArtifactSalvage;
        MaxArtifactStar = TaskContext.Instance().Config.AutoArtifactSalvageConfig.MaxArtifactStar;
        ResinPriorityList = config.ResinPriorityList;
        OriginalResinUseCount = config.OriginalResinUseCount;
        CondensedResinUseCount = config.CondensedResinUseCount;
        TransientResinUseCount = config.TransientResinUseCount;
        FragileResinUseCount = config.FragileResinUseCount;
        SpecifyResinUse = config.SpecifyResinUse;
    }
}