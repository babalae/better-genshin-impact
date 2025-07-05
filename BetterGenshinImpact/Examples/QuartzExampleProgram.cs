using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service.Quartz;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BetterGenshinImpact.Examples;

/// <summary>
/// Quartz.NET 动态任务管理示例程序
/// 演示如何在控制台应用中使用调度功能
/// </summary>
public class QuartzExampleProgram
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Quartz.NET 动态任务管理示例 ===\n");

        // 创建主机和服务
        var host = CreateHostBuilder(args).Build();
        
        try
        {
            // 启动主机
            await host.StartAsync();
            
            // 获取服务
            var schedulerManager = host.Services.GetRequiredService<SchedulerManager>();
            var dynamicTaskService = host.Services.GetRequiredService<DynamicTaskExampleService>();
            
            // 运行示例
            await RunExamples(schedulerManager, dynamicTaskService);
            
            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }
        finally
        {
            await host.StopAsync();
        }
    }
    
    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // 注册 Quartz.NET
                services.AddQuartz(q =>
                {
                    q.UseInMemoryStore();
                    q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 5);
                });
                
                // 注册自定义服务
                services.AddSingleton<SchedulerManager>();
                services.AddSingleton<DynamicTaskExampleService>();
                services.AddHostedService<QuartzHostedService>();
                
                // 注册模拟的脚本服务
                services.AddSingleton<MockScriptService>();
            });
    
    private static async Task RunExamples(SchedulerManager schedulerManager, DynamicTaskExampleService dynamicTaskService)
    {
        // 创建示例脚本组
        var scriptGroup1 = CreateExampleScriptGroup("每日任务组", "Daily");
        var scriptGroup2 = CreateExampleScriptGroup("每周任务组", "Monday");
        var scriptGroup3 = CreateExampleScriptGroup("自定义任务组", "0 0/15 * * * ? *"); // 每15分钟
        
        Console.WriteLine("1. 添加定时任务示例");
        Console.WriteLine("===================");
        
        // 示例1：添加每日任务
        Console.WriteLine("添加每日任务（每天8:30执行）...");
        bool success1 = await dynamicTaskService.CreateDailyTaskAsync(scriptGroup1, 8, 30);
        Console.WriteLine($"结果: {(success1 ? "成功" : "失败")}\n");
        
        // 示例2：添加每周任务
        Console.WriteLine("添加每周任务（每周一9:00执行）...");
        bool success2 = await dynamicTaskService.CreateWeeklyTaskAsync(scriptGroup2, 1, 9, 0);
        Console.WriteLine($"结果: {(success2 ? "成功" : "失败")}\n");
        
        // 示例3：添加自定义Cron表达式任务
        Console.WriteLine("添加自定义任务（每15分钟执行）...");
        bool success3 = await schedulerManager.AddScheduledTaskAsync(scriptGroup3, "0 0/15 * * * ? *");
        Console.WriteLine($"结果: {(success3 ? "成功" : "失败")}\n");
        
        // 示例4：批量添加任务
        Console.WriteLine("2. 批量操作示例");
        Console.WriteLine("================");
        var scriptGroups = new List<ScriptGroup> { scriptGroup1, scriptGroup2, scriptGroup3 };
        int batchCount = await dynamicTaskService.AddMultipleScriptGroupSchedulesAsync(scriptGroups, "0 0 12 * * ? *");
        Console.WriteLine($"批量添加任务结果: 成功添加 {batchCount} 个任务\n");
        
        // 示例5：查看所有任务
        Console.WriteLine("3. 查看任务状态");
        Console.WriteLine("===============");
        var allTasks = await schedulerManager.GetAllScheduledTasksAsync();
        Console.WriteLine($"当前总任务数: {allTasks.Count}");
        foreach (var task in allTasks)
        {
            Console.WriteLine($"- 任务: {task.JobName}");
            Console.WriteLine($"  脚本组: {task.ScriptGroupName}");
            Console.WriteLine($"  Cron表达式: {task.CronExpression}");
            Console.WriteLine($"  下次执行: {task.NextFireTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未安排"}");
            Console.WriteLine();
        }
        
        // 示例6：生成任务报告
        Console.WriteLine("4. 任务执行报告");
        Console.WriteLine("===============");
        var report = await dynamicTaskService.GetScheduledTaskReportAsync();
        Console.WriteLine($"总任务数: {report.TotalTasks}");
        Console.WriteLine($"活跃任务数: {report.ActiveTasks}");
        Console.WriteLine("按脚本组统计:");
        foreach (var group in report.TasksByScriptGroup)
        {
            Console.WriteLine($"  {group.Key}: {group.Value} 个任务");
        }
        Console.WriteLine("\n即将执行的任务:");
        foreach (var execution in report.NextExecutions.Take(5))
        {
            Console.WriteLine($"  {execution.ScriptGroupName} - {execution.NextFireTime:yyyy-MM-dd HH:mm:ss}");
        }
        Console.WriteLine();
        
        // 示例7：任务管理操作
        Console.WriteLine("5. 任务管理操作");
        Console.WriteLine("===============");
        
        // 暂停任务
        if (allTasks.Count > 0)
        {
            var firstTask = allTasks[0];
            Console.WriteLine($"暂停任务: {firstTask.JobName}");
            bool pauseResult = await schedulerManager.PauseScheduledTaskAsync(firstTask.JobName);
            Console.WriteLine($"暂停结果: {(pauseResult ? "成功" : "失败")}");
            
            // 等待一秒
            await Task.Delay(1000);
            
            // 恢复任务
            Console.WriteLine($"恢复任务: {firstTask.JobName}");
            bool resumeResult = await schedulerManager.ResumeScheduledTaskAsync(firstTask.JobName);
            Console.WriteLine($"恢复结果: {(resumeResult ? "成功" : "失败")}");
        }
        
        Console.WriteLine();
        
        // 示例8：更新任务
        if (allTasks.Count > 0)
        {
            var firstTask = allTasks[0];
            var newCron = "0 0 10 * * ? *"; // 每天10点执行
            Console.WriteLine($"更新任务Cron表达式: {firstTask.JobName}");
            Console.WriteLine($"新的Cron表达式: {newCron}");
            bool updateResult = await schedulerManager.UpdateScheduledTaskAsync(firstTask.JobName, newCron);
            Console.WriteLine($"更新结果: {(updateResult ? "成功" : "失败")}");
        }
        
        Console.WriteLine();
        
        // 示例9：清理任务
        Console.WriteLine("6. 清理任务");
        Console.WriteLine("===========");
        int cleanupCount = await dynamicTaskService.ClearAllScheduledTasksAsync();
        Console.WriteLine($"清理结果: 删除了 {cleanupCount} 个任务");
    }
    
    private static ScriptGroup CreateExampleScriptGroup(string name, string schedule)
    {
        var scriptGroup = new ScriptGroup
        {
            Name = name,
            Index = 1
        };
        
        // 添加示例项目
        var project = new ScriptGroupProject
        {
            Name = $"{name}_脚本",
            FolderName = "example",
            Type = "Javascript",
            Status = "Enabled",
            Schedule = schedule,
            Index = 1
        };
        
        scriptGroup.AddProject(project);
        return scriptGroup;
    }
}

/// <summary>
/// 模拟脚本服务，用于演示
/// </summary>
public class MockScriptService : BetterGenshinImpact.Service.Interface.IScriptService
{
    private readonly ILogger<MockScriptService> _logger;
    
    public MockScriptService(ILogger<MockScriptService> logger)
    {
        _logger = logger;
    }
    
    public async Task RunMulti(List<ScriptGroupProject> projects, string groupName, object? taskProgress)
    {
        _logger.LogInformation("模拟执行脚本组: {GroupName}，包含 {ProjectCount} 个项目", groupName, projects.Count);
        
        foreach (var project in projects)
        {
            _logger.LogInformation("执行项目: {ProjectName} ({ProjectType})", project.Name, project.Type);
            // 模拟执行时间
            await Task.Delay(100);
        }
        
        _logger.LogInformation("脚本组 {GroupName} 执行完成", groupName);
    }
}