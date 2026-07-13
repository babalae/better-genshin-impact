using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.LogParse;

public class ConfigGroupMerger
{
    // 定义一天的起始时间为凌晨4点
    private static readonly TimeSpan DayStartOffset = new TimeSpan(4, 0, 0);

    public List<LogParse.ConfigGroupEntity> MergeConfigGroups(List<LogParse.ConfigGroupEntity> groups)
    {
        if (groups == null || groups.Count == 0)
            return new List<LogParse.ConfigGroupEntity>();

        // 反转列表为正序处理（最早的在前）
        var reversedGroups = new List<LogParse.ConfigGroupEntity>(groups);
        reversedGroups.Reverse();

        List<LogParse.ConfigGroupEntity> result = new List<LogParse.ConfigGroupEntity>();
        LogParse.ConfigGroupEntity current = CloneGroup(reversedGroups[0]);

        for (int i = 1; i < reversedGroups.Count; i++)
        {
            LogParse.ConfigGroupEntity next = reversedGroups[i];

            // 检查名称相同且开始日期在同一天内
            if (current.Name == next.Name && 
                AreDatesInSameDay(current.StartDate, next.StartDate))
            {
                // 创建新的合并组对象
                current = new LogParse.ConfigGroupEntity
                {
                    Name = current.Name,
                    StartDate = MinDateTime(current.StartDate, next.StartDate),
                    EndDate = MaxDateTime(current.EndDate, next.EndDate),
                    ConfigTaskList = MergeTaskLists(current.ConfigTaskList, next.ConfigTaskList)
                };
            }
            else
            {
                // 保存当前组并开始新组
                result.Add(current);
                current = CloneGroup(next);
            }
        }

        result.Add(current);
        return result;
    }

    // 合并两个任务列表
    private List<LogParse.ConfigGroupEntity.ConfigTask> MergeTaskLists(
        List<LogParse.ConfigGroupEntity.ConfigTask> list1, 
        List<LogParse.ConfigGroupEntity.ConfigTask> list2)
    {
        if (list1.Count == 0) return CloneTaskList(list2);
        if (list2.Count == 0) return CloneTaskList(list1);

        var mergedList = new List<LogParse.ConfigGroupEntity.ConfigTask>(list1.Select(CloneTask));
        var clonedList2 = CloneTaskList(list2);

        // 获取连接处的任务
        var lastTask = mergedList.Last();
        var firstTask = clonedList2.First();

        if (lastTask.Name == firstTask.Name)
        {
            // 创建新的合并任务
            var mergedTask = new LogParse.ConfigGroupEntity.ConfigTask
            {
                IsMerger = true,
                Name = lastTask.Name,
                StartDate = MinDateTime(lastTask.StartDate, firstTask.StartDate),
                EndDate = MaxDateTime(lastTask.EndDate, firstTask.EndDate),
                Picks = MergePickDictionaries(lastTask.Picks, firstTask.Picks),
                Fault = (lastTask.StartDate ?? DateTime.MinValue) >= (firstTask.StartDate ?? DateTime.MinValue)
                    ? lastTask.Fault : firstTask.Fault
            };

            // 替换并连接列表
            mergedList[mergedList.Count - 1] = mergedTask;
            mergedList.AddRange(clonedList2.Skip(1));
        }
        else
        {
            mergedList.AddRange(clonedList2);
        }

        return mergedList;
    }

    // 合并拾取字典
    private Dictionary<string, int> MergePickDictionaries(
        Dictionary<string, int> dict1, 
        Dictionary<string, int> dict2)
    {
        var result = new Dictionary<string, int>();
        foreach (var kvp in dict1.Concat(dict2))
        {
            result[kvp.Key] = result.TryGetValue(kvp.Key, out int count) 
                ? count + kvp.Value 
                : kvp.Value;
        }
        return result;
    }

    // 判断两个日期是否在同一天（凌晨4点分界）
    private bool AreDatesInSameDay(DateTime? date1, DateTime? date2)
    {
        if (date1 == null || date2 == null) return false;
        return (date1.Value - DayStartOffset).Date == (date2.Value - DayStartOffset).Date;
    }

    // 辅助方法：取最小日期
    private DateTime? MinDateTime(DateTime? d1, DateTime? d2)
    {
        if (d1 == null) return d2;
        if (d2 == null) return d1;
        return d1 < d2 ? d1 : d2;
    }

    // 辅助方法：取最大日期
    private DateTime? MaxDateTime(DateTime? d1, DateTime? d2)
    {
        if (d1 == null) return d2;
        if (d2 == null) return d1;
        return d1 > d2 ? d1 : d2;
    }

    // 克隆配置组
    private LogParse.ConfigGroupEntity CloneGroup(LogParse.ConfigGroupEntity group)
    {
        return new LogParse.ConfigGroupEntity
        {
            Name = group.Name,
            StartDate = group.StartDate,
            EndDate = group.EndDate,
            ConfigTaskList = CloneTaskList(group.ConfigTaskList)
        };
    }

    // 克隆任务列表
    private List<LogParse.ConfigGroupEntity.ConfigTask> CloneTaskList(List<LogParse.ConfigGroupEntity.ConfigTask> tasks)
    {
        return tasks.Select(CloneTask).ToList();
    }

    // 克隆单个任务
    private LogParse.ConfigGroupEntity.ConfigTask CloneTask(LogParse.ConfigGroupEntity.ConfigTask task)
    {
        return new LogParse.ConfigGroupEntity.ConfigTask
        {
            IsMerger = task.IsMerger,
            Name = task.Name,
            StartDate = task.StartDate,
            EndDate = task.EndDate,
            Picks = new Dictionary<string, int>(task.Picks),
            Fault = task.Fault // 直接引用，不克隆（根据要求）
        };
    }
}

