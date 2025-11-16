using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BetterGenshinImpact.Model.Gear.Triggers.QuartzJob;

/// <summary>
/// Quartz.NET 任务定义
/// </summary>
[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public class QuartzGearTaskJob : IJob
{
    public static readonly JobKey Key = new("gear-task-job", "default-group");
    
    private readonly ILogger<QuartzGearTaskJob> _logger = App.GetLogger<QuartzGearTaskJob>();
    
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var jobDataMap = context.MergedJobDataMap;
            var triggerName = jobDataMap.GetString("TriggerName") ?? "Unknown";
            var triggerId = jobDataMap.GetString("TriggerId") ?? Guid.NewGuid().ToString();
            var taskDefinitionName = jobDataMap.GetString("TaskDefinitionName");

            if (string.IsNullOrWhiteSpace(taskDefinitionName))
            {
                _logger.LogWarning("触发器 {TriggerName} 未配置任务定义名称", triggerName);
                return;
            }

            var shouldInterrupt = jobDataMap.GetBooleanValue("ShouldInterruptOthers");
            if (shouldInterrupt)
            {
                await InterruptOtherJobs(context, triggerId);
            }

            var executor = App.GetRequiredService<GearTaskExecutor>();
            await executor.ExecuteTaskDefinitionAsync(taskDefinitionName, context.CancellationToken);

            _logger.LogInformation("触发器 {TriggerName} 的任务定义执行完成", triggerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行定时任务时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 中断其他同类型的定时任务
    /// </summary>
    private async Task InterruptOtherJobs(IJobExecutionContext context, string currentTriggerId)
    {
        try
        {
            var scheduler = context.Scheduler;
            var currentJobKey = context.JobDetail.Key;
            
            // 获取所有正在执行的任务
            var executingJobs = await scheduler.GetCurrentlyExecutingJobs();
            
            foreach (var executingJob in executingJobs)
            {
                // 跳过当前任务
                if (executingJob.JobDetail.Key.Equals(currentJobKey))
                    continue;
                
                // 检查是否是同类型的 GearTaskJob
                if (executingJob.JobDetail.JobType == typeof(QuartzGearTaskJob))
                {
                    var executingTriggerId = executingJob.JobDetail.JobDataMap.GetString("TriggerId");
                    if (executingTriggerId != currentTriggerId)
                    {
                        _logger.LogInformation("中断正在执行的任务: {JobKey}", executingJob.JobDetail.Key);
                        await scheduler.Interrupt(executingJob.JobDetail.Key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "中断其他任务时发生错误");
        }
    }
}