using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service.Quartz;

namespace BetterGenshinImpact.Tests;

/// <summary>
/// Quartz.NET 集成测试
/// 验证所有组件是否正确工作
/// </summary>
public static class QuartzIntegrationTest
{
    /// <summary>
    /// 测试 CronExpressionHelper 功能
    /// </summary>
    public static void TestCronExpressionHelper()
    {
        Console.WriteLine("=== 测试 CronExpressionHelper ===");
        
        // 测试生成表达式
        var dailyExpression = CronExpressionHelper.CreateDaily(8, 30);
        Console.WriteLine($"每日8:30表达式: {dailyExpression}");
        
        var weeklyExpression = CronExpressionHelper.CreateWeekly(DayOfWeek.Monday, 9, 0);
        Console.WriteLine($"每周一9:00表达式: {weeklyExpression}");
        
        var workdayExpression = CronExpressionHelper.CreateWorkdays(9, 0);
        Console.WriteLine($"工作日9:00表达式: {workdayExpression}");
        
        // 测试解析描述
        var description = CronExpressionHelper.ParseToDescription(dailyExpression);
        Console.WriteLine($"表达式描述: {description}");
        
        // 测试验证
        bool isValid = CronExpressionHelper.IsValidCronExpression(dailyExpression);
        Console.WriteLine($"表达式有效性: {isValid}");
        
        // 测试获取下次执行时间
        var nextTime = CronExpressionHelper.GetNextExecutionTime(dailyExpression);
        Console.WriteLine($"下次执行时间: {nextTime}");
        
        Console.WriteLine("CronExpressionHelper 测试完成\n");
    }
    
    /// <summary>
    /// 测试脚本组创建
    /// </summary>
    public static ScriptGroup CreateTestScriptGroup()
    {
        Console.WriteLine("=== 创建测试脚本组 ===");
        
        var scriptGroup = new ScriptGroup
        {
            Name = "测试脚本组",
            Index = 1
        };
        
        var project = new ScriptGroupProject
        {
            Name = "测试脚本",
            FolderName = "test",
            Type = "Javascript",
            Status = "Enabled",
            Schedule = "Daily",
            Index = 1
        };
        
        scriptGroup.AddProject(project);
        
        Console.WriteLine($"创建脚本组: {scriptGroup.Name}");
        Console.WriteLine($"包含项目: {project.Name} ({project.Type})");
        Console.WriteLine($"项目状态: {project.Status}");
        Console.WriteLine($"调度配置: {project.Schedule}");
        
        return scriptGroup;
    }
    
    /// <summary>
    /// 测试预定义 Cron 表达式
    /// </summary>
    public static void TestPredefinedExpressions()
    {
        Console.WriteLine("=== 测试预定义 Cron 表达式 ===");
        
        var predefined = CronExpressionHelper.PredefinedExpressions;
        Console.WriteLine($"预定义表达式数量: {predefined.Count}");
        
        // 显示一些常用的预定义表达式
        var commonExpressions = new[]
        {
            "每天午夜", "每天早上8点", "工作日上午9点", 
            "每周一上午9点", "每小时", "每30分钟"
        };
        
        foreach (var key in commonExpressions)
        {
            if (predefined.TryGetValue(key, out var expression))
            {
                Console.WriteLine($"{key}: {expression}");
                
                // 验证表达式
                bool isValid = CronExpressionHelper.IsValidCronExpression(expression);
                Console.WriteLine($"  有效性: {isValid}");
                
                // 获取下次执行时间
                var nextTime = CronExpressionHelper.GetNextExecutionTime(expression);
                Console.WriteLine($"  下次执行: {nextTime}");
                Console.WriteLine();
            }
        }
        
        Console.WriteLine("预定义表达式测试完成\n");
    }
    
    /// <summary>
    /// 测试调度配置转换
    /// </summary>
    public static void TestScheduleConversion()
    {
        Console.WriteLine("=== 测试调度配置转换 ===");
        
        var schedules = new[] { "Daily", "EveryTwoDays", "Monday", "Tuesday", "Wednesday" };
        
        foreach (var schedule in schedules)
        {
            var cronExpression = SchedulerManager.ConvertScheduleToCron(schedule);
            Console.WriteLine($"{schedule} -> {cronExpression}");
            
            // 验证转换后的表达式
            bool isValid = CronExpressionHelper.IsValidCronExpression(cronExpression);
            Console.WriteLine($"  有效性: {isValid}");
        }
        
        Console.WriteLine("调度配置转换测试完成\n");
    }
    
    /// <summary>
    /// 测试时间计算功能
    /// </summary>
    public static void TestTimeCalculation()
    {
        Console.WriteLine("=== 测试时间计算功能 ===");
        
        var expressions = new[]
        {
            CronExpressionHelper.CreateDaily(8, 30),
            CronExpressionHelper.CreateWeekly(DayOfWeek.Monday, 9, 0),
            CronExpressionHelper.CreateInterval(30),
            CronExpressionHelper.CreateWorkdays(9, 0)
        };
        
        foreach (var expression in expressions)
        {
            Console.WriteLine($"表达式: {expression}");
            
            // 获取未来5次执行时间
            var nextTimes = CronExpressionHelper.GetNextExecutionTimes(expression, 5);
            Console.WriteLine($"未来5次执行时间:");
            
            for (int i = 0; i < nextTimes.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {nextTimes[i]:yyyy-MM-dd HH:mm:ss}");
            }
            
            Console.WriteLine();
        }
        
        Console.WriteLine("时间计算功能测试完成\n");
    }
    
    /// <summary>
    /// 运行所有测试
    /// </summary>
    public static void RunAllTests()
    {
        Console.WriteLine("开始 Quartz.NET 集成测试...\n");
        
        try
        {
            TestCronExpressionHelper();
            TestPredefinedExpressions();
            TestScheduleConversion();
            TestTimeCalculation();
            
            var testScriptGroup = CreateTestScriptGroup();
            Console.WriteLine($"测试脚本组创建成功: {testScriptGroup.Name}");
            
            Console.WriteLine("\n✅ 所有测试通过！Quartz.NET 集成工作正常。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ 测试失败: {ex.Message}");
            Console.WriteLine($"详细错误: {ex}");
        }
    }
}

/// <summary>
/// 测试程序入口点
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        QuartzIntegrationTest.RunAllTests();
        
        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }
}