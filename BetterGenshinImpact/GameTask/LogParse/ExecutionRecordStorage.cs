using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.LogParse;

public class ExecutionRecordStorage
{
    private static readonly string StorageDirectory = Path.Combine(Global.Absolute(@"log"), "ExecutionRecords");

    /// <summary>
    /// 保存执行记录到对应日期的文件中
    /// </summary>
    public static void SaveExecutionRecord(ExecutionRecord record)
    {
        // 创建存储目录
        Directory.CreateDirectory(StorageDirectory);

        // 获取基于StartTime的日期文件名
        string dateKey = record.StartTime.ToString("yyyyMMdd");
        string fileName = $"{dateKey}.json";
        string filePath = Path.Combine(StorageDirectory, fileName);

        // 读取或创建当天的记录
        DailyExecutionRecord dailyRecord;
        if (File.Exists(filePath))
        {
            string json = File.ReadAllText(filePath);
            dailyRecord = JsonConvert.DeserializeObject<DailyExecutionRecord>(json);
        }
        else
        {
            dailyRecord = new DailyExecutionRecord
            {
                Name = dateKey
            };
        }

        // 更新或添加记录
        var existingIndex = dailyRecord.ExecutionRecords.FindIndex(r => r.Id == record.Id);
        if (existingIndex >= 0)
        {
            dailyRecord.ExecutionRecords[existingIndex] = record;
        }
        else
        {
            dailyRecord.ExecutionRecords.Add(record);
        }

        // 保存更新后的文件
        string updatedJson = JsonConvert.SerializeObject(dailyRecord, Formatting.Indented);
        File.WriteAllText(filePath, updatedJson);
    }

    public static List<DailyExecutionRecord> GetRecentExecutionRecordsByConfig(TaskCompletionSkipRuleConfig config)
    {

        // 确定边界时间是否有效（0-23之间）
        bool boundaryTimeEnable = config.BoundaryTime >= 0 && config.BoundaryTime <= 23;

        // 默认获取最近1天的执行记录
        int dayCount = 1;

        // 如果边界时间有效，需要获取2天的记录（可能跨越边界）
        if (boundaryTimeEnable)
        {
            dayCount = 2;
        }

        // 如果配置了有效的间隔秒数，根据秒数计算需要获取的天数（向上取整）
        if (config.LastRunGapSeconds >= 0)
        {
            dayCount = ConvertSecondsToDaysUp(config.LastRunGapSeconds);
        }

        return GetRecentExecutionRecords(dayCount);
    }

    /// <summary>
    /// 读取最近N天的执行记录
    /// </summary>
    public static List<DailyExecutionRecord> GetRecentExecutionRecords(int days)
    {
        if (days <= 0)
            throw new ArgumentException("Days must be a positive integer", nameof(days));

        var results = new List<DailyExecutionRecord>();
        var storageDir = new DirectoryInfo(StorageDirectory);

        // 确保目录存在
        if (!storageDir.Exists) return results;

        // 计算日期范围
        var endDate = DateTime.Today;
        var startDate = endDate.AddDays(-days + 1);

        // 遍历日期范围
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            string fileName = $"{date:yyyyMMdd}.json";
            string filePath = Path.Combine(StorageDirectory, fileName);

            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var record = JsonConvert.DeserializeObject<DailyExecutionRecord>(json);
                results.Add(record);
            }
        }
        
        //实际使用中，使用倒序，反转记录列表，变为倒序
        results.Reverse();
        foreach (var dailyRecord in results)
        {
            var records = dailyRecord.ExecutionRecords;
            // 反转执行记录，变为倒序
            records.Reverse();
        }

        return results;
    }

    /// <summary>
    /// 将秒数转换为天数（向上取整）
    /// </summary>
    /// <param name="seconds">总秒数</param>
    /// <returns>向上取整后的天数</returns>
    private static int ConvertSecondsToDaysUp(int seconds)
    {
        if (seconds <= 0) return 0;

        const int secondsPerDay = 86400; // 24 * 60 * 60
        double days = (double)seconds / secondsPerDay;
        return (int)Math.Ceiling(days);
    }

    /// <summary>
    /// 根据自定义的一天开始时间判断日期是否属于"今天"
    /// </summary>
    /// <param name="boundaryHour">分界时间（小时），0-23之间</param>
    /// <param name="targetDate">要判断的日期</param>
    /// <returns>如果属于"今天"则返回true，否则返回false</returns>
    private static bool IsTodayByBoundary(int boundaryHour, DateTime targetDate)
    {
        // 验证分界时间是否有效
        if (boundaryHour < 0 || boundaryHour > 23)
            throw new ArgumentOutOfRangeException(nameof(boundaryHour), "分界时间必须在0-23之间");

        DateTime now = DateTime.Now;

        // 计算今天的开始时间（根据分界时间）
        DateTime todayStart;
        if (now.Hour >= boundaryHour)
        {
            // 今天已经过了分界时间，今天的开始是今天的分界时间
            todayStart = new DateTime(now.Year, now.Month, now.Day, boundaryHour, 0, 0);
        }
        else
        {
            // 今天还没过分界时间，今天的开始是昨天的分界时间
            todayStart = new DateTime(now.Year, now.Month, now.Day, boundaryHour, 0, 0).AddDays(-1);
        }

        // 计算今天的结束时间（明天的开始时间）
        DateTime todayEnd = todayStart.AddDays(1);

        // 判断目标日期是否在今天的范围内
        return targetDate >= todayStart && targetDate < todayEnd;
    }

    public static bool IsSkipTask(ScriptGroupProject project, out string message,List<DailyExecutionRecord>? dailyRecords=null)
    {
        // 初始化消息字符串
        message = "";

        // 获取任务完成跳过规则配置
        var config = project.GroupInfo?.Config?.PathingConfig.TaskCompletionSkipRuleConfig;

        // 检查配置是否有效：配置不存在、未启用、边界时间无效且间隔时间无效
        if (config == null ||
            !config.Enable ||
            (config.BoundaryTime < 0 || config.BoundaryTime > 23) && config.LastRunGapSeconds < 0)
        {
            return false; // 配置无效，不执行跳过检查
        }

        // 确定边界时间是否有效（0-23之间）
        bool boundaryTimeEnable = config.BoundaryTime >= 0 && config.BoundaryTime <= 23;
        
        // 获取项目相关信息
        var groupName = project.GroupInfo?.Name ?? "";
        var folderName = project.FolderName;
        var projectName = project.Name;
        var projectType = project.Type;

        // 获取最近指定天数的执行记录
        dailyRecords ??= GetRecentExecutionRecordsByConfig(config);
        

        // 遍历每日记录
        foreach (var dailyRecord in dailyRecords)
        {
            var records = dailyRecord.ExecutionRecords;

            // 反转执行记录，变为倒序
           // records.Reverse();

            // 遍历每条执行记录
            foreach (var record in records)
            {
                
                // 跳过未成功的执行记录
                if (!record.IsSuccessful) continue;

                // 跳过类型或项目名称不匹配的记录
                if (record.Type != projectType || record.ProjectName != projectName) continue;

                var calcTime = record.EndTime;
                if (config.ReferencePoint == "StartTime")
                {
                    calcTime = record.StartTime;
                }
                
                // 如果配置了间隔时间，检查记录是否在时间间隔内
                if (config.LastRunGapSeconds >= 0)
                {
                    double secondsSinceLastRun = (DateTime.Now - calcTime).TotalSeconds;

                    // 跳过超过配置间隔时间的记录
                    if (secondsSinceLastRun > config.LastRunGapSeconds) continue;
                }

                // 检查记录是否在"今天"（根据边界时间定义）
                if (boundaryTimeEnable)
                {
                    // 如果记录不在"今天"，则跳过
                    if (!IsTodayByBoundary(config.BoundaryTime, record.StartTime)) continue;
                }

                bool isMatchFound = false;
                string matchReason = "";

                // 检查匹配策略
                if (config.SkipPolicy == "GroupPhysicalPathSkipPolicy" &&
                    groupName == record.GroupName &&
                    folderName == record.FolderName)
                {
                    // 组和物理路径匹配策略
                    matchReason = "组和物理路径匹配一致";
                    isMatchFound = true;
                }
                else if (config.SkipPolicy == "PhysicalPathSkipPolicy" &&
                         folderName == record.FolderName)
                {
                    // 物理路径匹配策略
                    matchReason = "物理路径相同";
                    isMatchFound = true;
                }
                else if (config.SkipPolicy == "SameNameSkipPolicy")
                {
                    // 名称匹配策略（只需要项目名称相同）
                    matchReason = "名称相同";
                    isMatchFound = true;
                }
                else
                {
                    // 未知的跳过策略
                    Console.WriteLine("ExecutionRecordStorage: 未预期的跳过策略！");
                    continue; // 继续检查下一条记录
                }

                if (isMatchFound)
                {
                    // 构建匹配消息
                    message = $"检查出满足跳过条件: {matchReason}";

                    // 添加时间相关信息
                    if (config.LastRunGapSeconds >= 0)
                    {
                        // 计算下次可执行时间
                        DateTime nextExecutionTime = calcTime.AddSeconds(config.LastRunGapSeconds);
                        message += $", 需在 {nextExecutionTime:yyyy-M-d H:mm:ss} 之后才能开始执行";
                    }
                    else if (boundaryTimeEnable)
                    {
                        message += $", 需在下一日 {config.BoundaryTime} 点后才能开始执行";
                    }

                    // 添加匹配记录的ID
                    message += $", 匹配记录 GUID={record.Id}";

                    return true; // 找到匹配记录，返回true
                }
            }
        }

// 未找到匹配记录
        return false;
    }
}