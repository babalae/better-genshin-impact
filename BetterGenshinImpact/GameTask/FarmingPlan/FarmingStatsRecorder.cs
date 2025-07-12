using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.GameTask.FarmingPlan;

/// <summary>
/// 锄地统计记录器
/// </summary>
public static class FarmingStatsRecorder
{
    public static readonly string LogDirectory = Global.Absolute(@"log\FarmingPlan");

    public static bool IsDailyFarmingLimitReached(FarmingSession farmingSession, out string message)
    {
        if (farmingSession.PrimaryTarget == "disable")
        {
            message = "";
            return false;
        }

        var dailyFarmingData = ReadDailyFarmingData();
        var config = TaskContext.Instance().Config.OtherConfig.FarmingPlanConfig;
        int dailyEliteCap = config.DailyEliteCap;
        int dailyMobCap = config.DailyMobCap;

        bool isEliteOverLimit = dailyFarmingData.TotalEliteMobCount >= dailyEliteCap;
        bool isNormalOverLimit = dailyFarmingData.TotalNormalMobCount >= dailyMobCap;

        var messages = new List<string>();
        if (isEliteOverLimit) messages.Add($"精英超上限:{dailyFarmingData.TotalEliteMobCount}/{dailyEliteCap}");
        if (isNormalOverLimit) messages.Add($"小怪超上限:{dailyFarmingData.TotalNormalMobCount}/{dailyMobCap}");

        // 两者都超限时直接返回
        if (isEliteOverLimit && isNormalOverLimit)
        {
            message = string.Join(",", messages);
            return false;
        }

        bool result = false;
    
        if (farmingSession.PrimaryTarget == "elite" && isEliteOverLimit)
        {
            result = true;
            if (farmingSession.NormalMobCount > 0) messages.Add("脚本主目标为精英");
        }
        else if (farmingSession.PrimaryTarget == "normal" && isNormalOverLimit)
        {
            result = true;
            if (farmingSession.EliteMobCount > 0) messages.Add("脚本主目标为小怪");
        }

        if (!result)
        {
            //精英到上限，本次小怪数量为0，跳过。小怪到上限，本次精英数量为0，跳过。
            result = (isEliteOverLimit && farmingSession.NormalMobCount == 0) ||
                     (isNormalOverLimit && farmingSession.EliteMobCount == 0);
        }
        message = string.Join(",", messages);
        return result;
    }

    /// <summary>
    /// 记录锄地统计数据
    /// </summary>
    /// <param name="session">本次锄地会话数据</param>
    /// <param name="route">本次锄地路径信息</param>
    public static void RecordFarmingSession(FarmingSession session, FarmingRouteInfo route)
    {
        try
        {
            DateTime now = DateTime.Now;
            DailyFarmingData dailyData = ReadDailyFarmingData();
            // 如果需要计数，则更新统计数据
            if (session.AllowFarmingCount)
            {
                dailyData.TotalNormalMobCount += session.NormalMobCount;
                dailyData.TotalEliteMobCount += session.EliteMobCount;
            }

            // 添加新的锄地记录
            dailyData.Records.Add(CreateFarmingRecord(session, route, now));

            // 保存更新后的数据
            SaveDailyData(dailyData.FilePath, dailyData);
            TaskControl.Logger.LogInformation(
                $"锄地进度:[小怪:{dailyData.TotalNormalMobCount}/{TaskContext.Instance().Config.OtherConfig.FarmingPlanConfig.DailyMobCap}" +
                $",精英:{dailyData.TotalEliteMobCount}/{TaskContext.Instance().Config.OtherConfig.FarmingPlanConfig.DailyEliteCap}]");
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogError($"锄地进度记录失败：{e.Message}");
        }
    }

    public static DailyFarmingData ReadDailyFarmingData()
    {
        // 确定统计日期（以凌晨4点为分界）
        DateTime now = DateTime.Now;
        DateTime statsDate = CalculateStatsDate(now);
        string dateString = statsDate.ToString("yyyyMMdd");

        // 确保目录存在
        Directory.CreateDirectory(LogDirectory);
        string filePath = Path.Combine(LogDirectory, $"{dateString}.json");

        // 读取现有数据或创建新数据
        DailyFarmingData dailyData = LoadDailyData(filePath);
        return dailyData;
    }

    /// <summary>
    /// 计算统计日期（凌晨4点为分界）
    /// </summary>
    private static DateTime CalculateStatsDate(DateTime currentTime)
    {
        // 如果当前时间在4点之前，则算作前一天
        return currentTime.Hour < 4 ? currentTime.Date.AddDays(-1) : currentTime.Date;
    }

    /// <summary>
    /// 创建新的锄地记录
    /// </summary>
    private static FarmingRecord CreateFarmingRecord(FarmingSession session, FarmingRouteInfo route, DateTime timestamp)
    {
        return new FarmingRecord
        {
            GroupName = route.GroupName,
            ProjectName = route.ProjectName,
            FolderName = route.FolderName,
            NormalMobCount = session.AllowFarmingCount ? session.NormalMobCount : 0,
            EliteMobCount = session.AllowFarmingCount ? session.EliteMobCount : 0,
            Timestamp = timestamp
        };
    }

    /// <summary>
    /// 加载每日数据
    /// </summary>
    private static DailyFarmingData LoadDailyData(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new DailyFarmingData()
            {
                FilePath = filePath
            };
        }

        try
        {
            string json = File.ReadAllText(filePath);
            DailyFarmingData dailyFarmingData = JsonConvert.DeserializeObject<DailyFarmingData>(json, GetJsonSettings())
                                                ?? new DailyFarmingData();
            dailyFarmingData.FilePath = filePath;
            return dailyFarmingData;
        }
        catch (JsonException)
        {
            // 文件损坏时创建新数据
            return new DailyFarmingData()
            {
                FilePath = filePath
            };
        }
    }

    /// <summary>
    /// 保存每日数据
    /// </summary>
    private static void SaveDailyData(string filePath, DailyFarmingData data)
    {
        string json = JsonConvert.SerializeObject(data, GetJsonSettings());
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 获取JSON序列化设置
    /// </summary>
    private static JsonSerializerSettings GetJsonSettings()
    {
        return new JsonSerializerSettings
        {
            // 使用下划线命名法
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            },
            Formatting = Formatting.Indented,
            DateFormatHandling = DateFormatHandling.IsoDateFormat
        };
    }
}