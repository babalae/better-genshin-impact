using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.LogParse;
using BetterGenshinImpact.Helpers;
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
    public static bool debugMode = false;
    
    //输出测试用
    public static void debugInfo(string msg)
    {
        if (debugMode)
        {
            TaskControl.Logger.LogDebug(msg);
        }
    }

    public static bool IsDailyFarmingLimitReached(FarmingSession farmingSession, out string message)
    {
        if (!farmingSession.AllowFarmingCount || farmingSession.PrimaryTarget == "disable")
        {
            message = "";
            return false;
        }

        var dailyFarmingData = ReadDailyFarmingData();
        var config = TaskContext.Instance().Config.OtherConfig.FarmingPlanConfig;
        var mysdCfg = config.MiyousheDataConfig;
        bool mysEnable = mysdCfg.Enabled;
        var cap = dailyFarmingData.getFinalCap();
        var ft = dailyFarmingData.getFinalTotalMobCount();
        
        var dailyEliteCap = cap.DailyEliteCap;
        var dailyMobCap = cap.DailyMobCap;
        var totalEliteMobCount = ft.TotalEliteMobCount;
        var totalNormalMobCount = ft.TotalNormalMobCount;
        
        bool isEliteOverLimit = totalEliteMobCount >= dailyEliteCap;
        bool isNormalOverLimit = totalNormalMobCount >= dailyMobCap;

        var messages = new List<string>();
        if (isEliteOverLimit) messages.Add($"精英超上限:{totalEliteMobCount}/{dailyEliteCap}");
        if (isNormalOverLimit) messages.Add($"小怪超上限:{totalNormalMobCount}/{dailyMobCap}");
        
        //尝试更新米游社的条件，超过最后札记两个小时，并且超过上次尝试更新20分钟
        debugInfo($"尝试更新米游社：{DateTime.Now} > {dailyFarmingData.TravelsDiaryDetailManagerUpdateTime.AddHours(2)}&&{DateTime.Now}> {dailyFarmingData.LastMiyousheUpdateTime.AddMinutes(20)}");
        if (mysEnable
            &&DateTime.Now > dailyFarmingData.TravelsDiaryDetailManagerUpdateTime.AddHours(2)
            && DateTime.Now > dailyFarmingData.LastMiyousheUpdateTime.AddMinutes(20))
        {
            Task.Run(() => TryUpdateTravelsData());
        }

        
        // 两者都超限时直接返回
        if (isEliteOverLimit && isNormalOverLimit)
        {
            message = string.Join(",", messages);
            return true;
        }

        if ( farmingSession.NormalMobCount == 0 && farmingSession.EliteMobCount ==0)
        {
            messages.Add("精英和小怪计数都为0，请确认配置");
            message = string.Join(",", messages);
            return true;  
        }

        if ((farmingSession.EliteMobCount == 0 && farmingSession.PrimaryTarget == "elite")
            ||(farmingSession.NormalMobCount == 0 && farmingSession.PrimaryTarget == "normal"))
        {
            messages.Add("主目标计数为0，请确认配置");
            message = string.Join(",", messages);
            return true;  
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
            var ft = dailyData.getFinalTotalMobCount();
            var cap = dailyData.getFinalCap();
            // 保存更新后的数据
            SaveDailyData(dailyData.FilePath, dailyData);
            TaskControl.Logger.LogInformation(
                $"锄地进度:[小怪:{ft.TotalNormalMobCount}/{cap.DailyMobCap}" +
                $",精英:{ft.TotalEliteMobCount}/{cap.DailyEliteCap}]"+(dailyData.EnableMiyousheStats()?"(合并米游社数据)":""));
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogError($"锄地进度记录失败：{e.Message}");
        }
    }
    
    //生成需要读取的札记月份
    private static readonly SemaphoreSlim _updateLock = new SemaphoreSlim(1, 1);
    private static bool _isUpdating = false;
    
    public async static Task TryUpdateTravelsData()
    {
        // 快速检查：如果正在更新则立即退出
        if (_isUpdating)
            return;
        try
        {
            _isUpdating = true;
           // await _updateLock.WaitAsync(); // 获取独占锁    
            debugInfo("开始更新米游社札记");
            string cookie = TaskContext.Instance().Config.OtherConfig.MiyousheConfig.Cookie;
            DailyFarmingData? dailyFarmingData = null;
            if (TaskContext.Instance().Config.OtherConfig.FarmingPlanConfig.MiyousheDataConfig.Enabled
                && cookie != string.Empty)
            {
                try
                {
                    GameInfo gameInfo = await TravelsDiaryDetailManager.UpdateTravelsDiaryDetailManager(cookie,true);
                    List<ActionItem> actionItems = TravelsDiaryDetailManager.loadNowDayActionItems(gameInfo);
                    //当天的数据
                    MoraStatistics ms = new MoraStatistics();
                    ms.ActionItems.AddRange(actionItems);
                    dailyFarmingData = ReadDailyFarmingData();
                    
                    if (actionItems.Count > 0)
                    {
                        dailyFarmingData.MiyousheTotalEliteMobCount = ms.EliteGameStatistics;
                        dailyFarmingData.MiyousheTotalNormalMobCount = ms.SmallMonsterStatistics;
                        dailyFarmingData.TravelsDiaryDetailManagerUpdateTime = DateTime.Parse(actionItems.Last().Time);
                        debugInfo($"札记当天数据：[精英：{dailyFarmingData.MiyousheTotalEliteMobCount},小怪：{dailyFarmingData.MiyousheTotalNormalMobCount},{dailyFarmingData.TravelsDiaryDetailManagerUpdateTime}]");
                    }
                    else
                    {
                        TaskControl.Logger.LogError($"米游社旅行札记未有数据！");
                    }
                }
                catch (Exception e)
                {
                    TaskControl.Logger.LogError($"米游社数据更新失败，请检查cookie是否过期：{e.Message}");
                }
            }

            if (dailyFarmingData == null)
            {
                dailyFarmingData = ReadDailyFarmingData();
            }

            dailyFarmingData.LastMiyousheUpdateTime = DateTime.Now;
            SaveDailyData(dailyFarmingData.FilePath, dailyFarmingData);
        }
        finally
        {
            //_updateLock.Release();
            _isUpdating = false;
        }
    }

    public static DailyFarmingData ReadDailyFarmingData()
    {
        // 确定统计日期（以凌晨4点为分界）
        DateTimeOffset now = ServerTimeHelper.GetServerTimeNow();
        DateTimeOffset statsDate = CalculateStatsDate(now);
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
    private static DateTime CalculateStatsDate(DateTimeOffset currentTime)
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