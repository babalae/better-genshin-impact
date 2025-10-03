using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.GameTask.Model;
using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.ClearScript;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainParam : BaseTaskParam<AutoDomainParam>
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

    public AutoDomainParam(int domainRoundNum, string path) : base(null, null)
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

    /// <summary>  
    /// 从JS请求参数构建任务参数  
    /// </summary>  
    /// <param name="config"></param>  
    /// <returns></returns>  
    public static AutoDomainParam BuildFromSoloTaskConfig(object config)
    {
        var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
        if (taskSettingsPageViewModel == null)
        {
            throw new ArgumentNullException(nameof(taskSettingsPageViewModel), "内部视图模型对象为空");
        }

        var jsObject = (ScriptObject)config;

 
        string strategyPath;
        var customStrategyName = ScriptObjectConverter.GetValue(jsObject, "strategyName", "");

        if (string.IsNullOrEmpty(customStrategyName))
        {
            // 未指定战斗策略参数，使用"根据队伍自动选择"  
            if (taskSettingsPageViewModel.GetFightStrategy("根据队伍自动选择", out strategyPath))
            {
                throw new InvalidOperationException("获取默认战斗策略失败");
            }
        }
        else
        {
            // 指定了战斗策略，直接拼接路径  
            strategyPath = Global.Absolute(@"User\AutoFight\" + customStrategyName + ".txt");

            // 验证文件是否存在  
            if (!File.Exists(strategyPath))
            {
                throw new InvalidOperationException($"战斗策略文件不存在: {strategyPath}");
            }
        }

        var domainRoundNum = ScriptObjectConverter.GetValue(jsObject, "domainRoundNum", 0);
        var param = new AutoDomainParam(domainRoundNum, strategyPath);

        // 设置其他参数  
        param.PartyName = ScriptObjectConverter.GetValue(jsObject, "partyName", param.PartyName);
        param.DomainName = ScriptObjectConverter.GetValue(jsObject, "domainName", param.DomainName);
        param.AutoArtifactSalvage = ScriptObjectConverter.GetValue(jsObject, "autoArtifactSalvage", param.AutoArtifactSalvage);
        param.MaxArtifactStar = ScriptObjectConverter.GetValue(jsObject, "maxArtifactStar", param.MaxArtifactStar);
        param.SpecifyResinUse = ScriptObjectConverter.GetValue(jsObject, "specifyResinUse", param.SpecifyResinUse);
        param.OriginalResinUseCount = ScriptObjectConverter.GetValue(jsObject, "originalResinUseCount", param.OriginalResinUseCount);
        param.CondensedResinUseCount = ScriptObjectConverter.GetValue(jsObject, "condensedResinUseCount", param.CondensedResinUseCount);
        param.TransientResinUseCount = ScriptObjectConverter.GetValue(jsObject, "transientResinUseCount", param.TransientResinUseCount);
        param.FragileResinUseCount = ScriptObjectConverter.GetValue(jsObject, "fragileResinUseCount", param.FragileResinUseCount);

        return param;
    }
}