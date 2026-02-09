using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.GameTask.AutoDomain.Model;

/// <summary>
/// 树脂使用记录
/// 用于自动秘境指定树脂刷取次数时候，计算剩余刷取次数
/// </summary>
public class ResinUseRecord
{
    public string Name { get; set; }
    
    public int RemainCount { get; set; }
    
    public int MaxCount { get; set; }

    public ResinUseRecord(string name, int maxCount)
    {
        Name = name;
        RemainCount = maxCount;
        MaxCount = maxCount;
    }

    public static List<ResinUseRecord> BuildFromDomainParam(AutoDomainParam taskParam)
    {
        List<ResinUseRecord> list = [];
        if (taskParam.SpecifyResinUse)
        {
            if (taskParam.CondensedResinUseCount > 0)
            {
                list.Add(new ResinUseRecord(Lang.S["GameTask_10385_a7b73a"], taskParam.CondensedResinUseCount));
            }
            if (taskParam.OriginalResinUseCount > 0)
            {
                list.Add(new ResinUseRecord(Lang.S["GameTask_10384_9fa864"], taskParam.OriginalResinUseCount));
            }
            if (taskParam.TransientResinUseCount > 0)
            {
                list.Add(new ResinUseRecord(Lang.S["OneDragon_047_6fe57c"], taskParam.TransientResinUseCount));
            }
            if (taskParam.FragileResinUseCount > 0)
            {
                list.Add(new ResinUseRecord(Lang.S["GameTask_10388_ad104b"], taskParam.FragileResinUseCount));
            }

            if (list.Count == 0)
            {
                throw new Exception(Lang.S["GameTask_10481_fd87af"]);
            }
        }

        return list;
    }
    
    public static List<ResinUseRecord> BuildFromDomainParam(AutoStygianOnslaughtConfig taskParam)
    {
        List<ResinUseRecord> list = [];
        if (taskParam.SpecifyResinUse)
        {
            if (taskParam.CondensedResinUseCount > 0)
            {
                list.Add(new ResinUseRecord(Lang.S["GameTask_10385_a7b73a"], taskParam.CondensedResinUseCount));
            }
            if (taskParam.OriginalResinUseCount > 0)
            {
                list.Add(new ResinUseRecord(Lang.S["GameTask_10384_9fa864"], taskParam.OriginalResinUseCount));
            }
            if (taskParam.TransientResinUseCount > 0)
            {
                list.Add(new ResinUseRecord(Lang.S["OneDragon_047_6fe57c"], taskParam.TransientResinUseCount));
            }
            if (taskParam.FragileResinUseCount > 0)
            {
                list.Add(new ResinUseRecord(Lang.S["GameTask_10388_ad104b"], taskParam.FragileResinUseCount));
            }

            if (list.Count == 0)
            {
                throw new Exception(Lang.S["GameTask_10481_fd87af"]);
            }
        }

        return list;
    }
}