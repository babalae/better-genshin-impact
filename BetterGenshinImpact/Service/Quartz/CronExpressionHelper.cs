using System;
using System.Collections.Generic;
using System.Globalization;

namespace BetterGenshinImpact.Service.Quartz;

/// <summary>
/// Cron 表达式帮助类
/// 提供常用的 Cron 表达式生成和解析功能
/// </summary>
public static class CronExpressionHelper
{
    /// <summary>
    /// 预定义的 Cron 表达式集合
    /// </summary>
    public static readonly Dictionary<string, string> PredefinedExpressions = new()
    {
        // 基本时间间隔
        { "每分钟", "0 * * * * ? *" },
        { "每5分钟", "0 0/5 * * * ? *" },
        { "每10分钟", "0 0/10 * * * ? *" },
        { "每15分钟", "0 0/15 * * * ? *" },
        { "每30分钟", "0 0/30 * * * ? *" },
        { "每小时", "0 0 * * * ? *" },
        { "每2小时", "0 0 0/2 * * ? *" },
        { "每4小时", "0 0 0/4 * * ? *" },
        { "每6小时", "0 0 0/6 * * ? *" },
        { "每12小时", "0 0 0/12 * * ? *" },
        
        // 每日时间点
        { "每天午夜", "0 0 0 * * ? *" },
        { "每天早上6点", "0 0 6 * * ? *" },
        { "每天早上8点", "0 0 8 * * ? *" },
        { "每天上午9点", "0 0 9 * * ? *" },
        { "每天中午12点", "0 0 12 * * ? *" },
        { "每天下午6点", "0 0 18 * * ? *" },
        { "每天晚上9点", "0 0 21 * * ? *" },
        { "每天晚上11点", "0 0 23 * * ? *" },
        
        // 工作日和周末
        { "工作日上午9点", "0 0 9 ? * MON-FRI *" },
        { "工作日下午6点", "0 0 18 ? * MON-FRI *" },
        { "周末上午10点", "0 0 10 ? * SAT,SUN *" },
        
        // 每周特定时间
        { "每周一上午9点", "0 0 9 ? * MON *" },
        { "每周二上午9点", "0 0 9 ? * TUE *" },
        { "每周三上午9点", "0 0 9 ? * WED *" },
        { "每周四上午9点", "0 0 9 ? * THU *" },
        { "每周五上午9点", "0 0 9 ? * FRI *" },
        { "每周六上午10点", "0 0 10 ? * SAT *" },
        { "每周日上午10点", "0 0 10 ? * SUN *" },
        
        // 每月特定时间
        { "每月1号午夜", "0 0 0 1 * ? *" },
        { "每月15号午夜", "0 0 0 15 * ? *" },
        { "每月最后一天", "0 0 0 L * ? *" },
        { "每月第一个周一", "0 0 0 ? * MON#1 *" },
        { "每月最后一个周五", "0 0 0 ? * FRIL *" },
        
        // 季度和年度
        { "每季度第一天", "0 0 0 1 1/3 ? *" },
        { "每年1月1日", "0 0 0 1 1 ? *" },
        { "每年生日提醒", "0 0 9 1 1 ? *" } // 示例：每年1月1日上午9点
    };

    /// <summary>
    /// 创建每日执行的 Cron 表达式
    /// </summary>
    /// <param name="hour">小时 (0-23)</param>
    /// <param name="minute">分钟 (0-59)</param>
    /// <param name="second">秒 (0-59，默认0)</param>
    /// <returns>Cron 表达式</returns>
    public static string CreateDaily(int hour, int minute, int second = 0)
    {
        ValidateTime(hour, minute, second);
        return $"{second} {minute} {hour} * * ? *";
    }

    /// <summary>
    /// 创建每周执行的 Cron 表达式
    /// </summary>
    /// <param name="dayOfWeek">星期几 (1=周一, 7=周日)</param>
    /// <param name="hour">小时 (0-23)</param>
    /// <param name="minute">分钟 (0-59)</param>
    /// <param name="second">秒 (0-59，默认0)</param>
    /// <returns>Cron 表达式</returns>
    public static string CreateWeekly(DayOfWeek dayOfWeek, int hour, int minute, int second = 0)
    {
        ValidateTime(hour, minute, second);
        var dayNames = new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
        var dayName = dayNames[(int)dayOfWeek];
        return $"{second} {minute} {hour} ? * {dayName} *";
    }

    /// <summary>
    /// 创建每月执行的 Cron 表达式
    /// </summary>
    /// <param name="dayOfMonth">月中的第几天 (1-31)</param>
    /// <param name="hour">小时 (0-23)</param>
    /// <param name="minute">分钟 (0-59)</param>
    /// <param name="second">秒 (0-59，默认0)</param>
    /// <returns>Cron 表达式</returns>
    public static string CreateMonthly(int dayOfMonth, int hour, int minute, int second = 0)
    {
        if (dayOfMonth < 1 || dayOfMonth > 31)
            throw new ArgumentException("月中的天数必须在1-31之间", nameof(dayOfMonth));
        
        ValidateTime(hour, minute, second);
        return $"{second} {minute} {hour} {dayOfMonth} * ? *";
    }

    /// <summary>
    /// 创建间隔执行的 Cron 表达式
    /// </summary>
    /// <param name="intervalMinutes">间隔分钟数</param>
    /// <param name="startMinute">开始分钟 (默认0)</param>
    /// <returns>Cron 表达式</returns>
    public static string CreateInterval(int intervalMinutes, int startMinute = 0)
    {
        if (intervalMinutes <= 0 || intervalMinutes > 59)
            throw new ArgumentException("间隔分钟数必须在1-59之间", nameof(intervalMinutes));
        
        if (startMinute < 0 || startMinute > 59)
            throw new ArgumentException("开始分钟必须在0-59之间", nameof(startMinute));

        return $"0 {startMinute}/{intervalMinutes} * * * ? *";
    }

    /// <summary>
    /// 创建工作日执行的 Cron 表达式
    /// </summary>
    /// <param name="hour">小时 (0-23)</param>
    /// <param name="minute">分钟 (0-59)</param>
    /// <param name="second">秒 (0-59，默认0)</param>
    /// <returns>Cron 表达式</returns>
    public static string CreateWorkdays(int hour, int minute, int second = 0)
    {
        ValidateTime(hour, minute, second);
        return $"{second} {minute} {hour} ? * MON-FRI *";
    }

    /// <summary>
    /// 创建周末执行的 Cron 表达式
    /// </summary>
    /// <param name="hour">小时 (0-23)</param>
    /// <param name="minute">分钟 (0-59)</param>
    /// <param name="second">秒 (0-59，默认0)</param>
    /// <returns>Cron 表达式</returns>
    public static string CreateWeekends(int hour, int minute, int second = 0)
    {
        ValidateTime(hour, minute, second);
        return $"{second} {minute} {hour} ? * SAT,SUN *";
    }

    /// <summary>
    /// 创建多个时间点执行的 Cron 表达式
    /// </summary>
    /// <param name="hours">小时数组</param>
    /// <param name="minute">分钟</param>
    /// <param name="second">秒 (默认0)</param>
    /// <returns>Cron 表达式</returns>
    public static string CreateMultipleHours(int[] hours, int minute, int second = 0)
    {
        if (hours == null || hours.Length == 0)
            throw new ArgumentException("小时数组不能为空", nameof(hours));

        foreach (var hour in hours)
        {
            if (hour < 0 || hour > 23)
                throw new ArgumentException($"小时 {hour} 必须在0-23之间", nameof(hours));
        }

        if (minute < 0 || minute > 59)
            throw new ArgumentException("分钟必须在0-59之间", nameof(minute));

        if (second < 0 || second > 59)
            throw new ArgumentException("秒必须在0-59之间", nameof(second));

        var hoursStr = string.Join(",", hours);
        return $"{second} {minute} {hoursStr} * * ? *";
    }

    /// <summary>
    /// 解析 Cron 表达式为人类可读的描述
    /// </summary>
    /// <param name="cronExpression">Cron 表达式</param>
    /// <returns>描述文本</returns>
    public static string ParseToDescription(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return "无效的Cron表达式";

        // 查找预定义表达式
        foreach (var predefined in PredefinedExpressions)
        {
            if (predefined.Value.Equals(cronExpression, StringComparison.OrdinalIgnoreCase))
            {
                return predefined.Key;
            }
        }

        try
        {
            var parts = cronExpression.Trim().Split(' ');
            if (parts.Length < 6)
                return "格式不正确的Cron表达式";

            var second = parts[0];
            var minute = parts[1];
            var hour = parts[2];
            var day = parts[3];
            var month = parts[4];
            var dayOfWeek = parts[5];

            var description = "自定义时间: ";

            // 解析秒
            if (second != "*" && second != "0")
            {
                description += $"第{second}秒 ";
            }

            // 解析分钟
            if (minute.Contains("/"))
            {
                var intervalParts = minute.Split('/');
                if (intervalParts.Length == 2 && intervalParts[0] == "0")
                {
                    description += $"每{intervalParts[1]}分钟 ";
                }
            }
            else if (minute != "*")
            {
                description += $"{minute}分 ";
            }

            // 解析小时
            if (hour.Contains("/"))
            {
                var intervalParts = hour.Split('/');
                if (intervalParts.Length == 2 && intervalParts[0] == "0")
                {
                    description += $"每{intervalParts[1]}小时 ";
                }
            }
            else if (hour.Contains(","))
            {
                description += $"在{hour.Replace(",", "、")}点 ";
            }
            else if (hour != "*")
            {
                description += $"{hour}点 ";
            }

            // 解析星期
            if (dayOfWeek != "*" && dayOfWeek != "?")
            {
                var dayNames = new Dictionary<string, string>
                {
                    { "MON", "周一" }, { "TUE", "周二" }, { "WED", "周三" }, { "THU", "周四" },
                    { "FRI", "周五" }, { "SAT", "周六" }, { "SUN", "周日" },
                    { "MON-FRI", "工作日" }, { "SAT,SUN", "周末" }
                };

                if (dayNames.TryGetValue(dayOfWeek, out var dayName))
                {
                    description += $"({dayName}) ";
                }
                else
                {
                    description += $"({dayOfWeek}) ";
                }
            }

            // 解析月中的天
            if (day != "*" && day != "?")
            {
                if (day == "L")
                {
                    description += "(月末) ";
                }
                else
                {
                    description += $"(每月{day}号) ";
                }
            }

            return description.Trim();
        }
        catch
        {
            return "无法解析的Cron表达式";
        }
    }

    /// <summary>
    /// 验证 Cron 表达式是否有效
    /// </summary>
    /// <param name="cronExpression">Cron 表达式</param>
    /// <returns>是否有效</returns>
    public static bool IsValidCronExpression(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return false;

        try
        {
            // 使用 Quartz.NET 的 CronExpression 进行验证
            var expression = new Quartz.CronExpression(cronExpression);
            return expression.IsSatisfiedBy(DateTime.Now);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取 Cron 表达式的下次执行时间
    /// </summary>
    /// <param name="cronExpression">Cron 表达式</param>
    /// <param name="fromTime">起始时间（可选，默认为当前时间）</param>
    /// <returns>下次执行时间，如果表达式无效则返回null</returns>
    public static DateTime? GetNextExecutionTime(string cronExpression, DateTime? fromTime = null)
    {
        try
        {
            var expression = new Quartz.CronExpression(cronExpression);
            var baseTime = fromTime ?? DateTime.Now;
            var nextTime = expression.GetNextValidTimeAfter(baseTime);
            return nextTime?.DateTime;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取 Cron 表达式的多个执行时间
    /// </summary>
    /// <param name="cronExpression">Cron 表达式</param>
    /// <param name="count">获取的执行时间数量</param>
    /// <param name="fromTime">起始时间（可选，默认为当前时间）</param>
    /// <returns>执行时间列表</returns>
    public static List<DateTime> GetNextExecutionTimes(string cronExpression, int count, DateTime? fromTime = null)
    {
        var times = new List<DateTime>();
        
        try
        {
            var expression = new Quartz.CronExpression(cronExpression);
            var currentTime = fromTime ?? DateTime.Now;
            
            for (int i = 0; i < count; i++)
            {
                var nextTime = expression.GetNextValidTimeAfter(currentTime);
                if (nextTime.HasValue)
                {
                    times.Add(nextTime.Value.DateTime);
                    currentTime = nextTime.Value.DateTime;
                }
                else
                {
                    break;
                }
            }
        }
        catch
        {
            // 表达式无效，返回空列表
        }
        
        return times;
    }

    private static void ValidateTime(int hour, int minute, int second)
    {
        if (hour < 0 || hour > 23)
            throw new ArgumentException("小时必须在0-23之间", nameof(hour));
        
        if (minute < 0 || minute > 59)
            throw new ArgumentException("分钟必须在0-59之间", nameof(minute));
        
        if (second < 0 || second > 59)
            throw new ArgumentException("秒必须在0-59之间", nameof(second));
    }
}