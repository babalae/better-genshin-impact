using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;

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
                list.Add(new ResinUseRecord("浓缩树脂", taskParam.CondensedResinUseCount));
            }
            if (taskParam.OriginalResinUseCount > 0)
            {
                list.Add(new ResinUseRecord("原粹树脂", taskParam.OriginalResinUseCount));
            }
            if (taskParam.TransientResinUseCount > 0)
            {
                list.Add(new ResinUseRecord("须臾树脂", taskParam.TransientResinUseCount));
            }
            if (taskParam.FragileResinUseCount > 0)
            {
                list.Add(new ResinUseRecord("脆弱树脂", taskParam.FragileResinUseCount));
            }

            if (list.Count == 0)
            {
                throw new Exception("你选择了指定树脂刷取次数，请至少配置一种树脂的刷取次数！");
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
                list.Add(new ResinUseRecord("浓缩树脂", taskParam.CondensedResinUseCount));
            }
            if (taskParam.OriginalResinUseCount > 0)
            {
                list.Add(new ResinUseRecord("原粹树脂", taskParam.OriginalResinUseCount));
            }
            if (taskParam.TransientResinUseCount > 0)
            {
                list.Add(new ResinUseRecord("须臾树脂", taskParam.TransientResinUseCount));
            }
            if (taskParam.FragileResinUseCount > 0)
            {
                list.Add(new ResinUseRecord("脆弱树脂", taskParam.FragileResinUseCount));
            }

            if (list.Count == 0)
            {
                throw new Exception("你选择了指定树脂刷取次数，请至少配置一种树脂的刷取次数！");
            }
        }

        return list;
    }
}