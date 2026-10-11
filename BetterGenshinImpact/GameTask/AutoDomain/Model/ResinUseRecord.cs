using System;
using System.Collections.Generic;
using System.Linq;
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
            if (taskParam.OriginalResin40UseCount > 0)
            {
                list.Add(new ResinUseRecord("原粹树脂40", taskParam.OriginalResin40UseCount));
            }
            if (taskParam.OriginalResin20UseCount > 0)
            {
                list.Add(new ResinUseRecord("原粹树脂20", taskParam.OriginalResin20UseCount));
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

            list = OrderByPriority(list, taskParam.ResinPriorityList);
        }

        return list;
    }

    /// <summary>
    /// 按配置的优先级重排默认加入顺序。优先级里没写的树脂排在后面并保持原相对顺序，
    /// </summary>
    private static List<ResinUseRecord> OrderByPriority(List<ResinUseRecord> list, List<string>? priorityOrder)
    {
        if (priorityOrder is not { Count: > 0 })
        {
            return list;
        }

        return [.. list.OrderBy(o =>
        {
            var index = priorityOrder.IndexOf(o.Name);
            return index < 0 ? int.MaxValue : index;
        })];
    }
    
    public static List<ResinUseRecord> BuildFromDomainParam(AutoStygianOnslaughtParam taskParam)
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