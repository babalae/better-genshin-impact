using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.GearTask;
using Microsoft.Extensions.Logging;
using Quartz;
using Serilog.Context;

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
        var jobDataMap = context.MergedJobDataMap;
        var triggerName = jobDataMap.GetString("TriggerName") ?? "Unknown";
        var triggerId = jobDataMap.GetString("TriggerId") ?? Guid.NewGuid().ToString();
        var taskDefinitionName = jobDataMap.GetString("TaskDefinitionName");
        var correlationId = Guid.NewGuid().ToString();
        
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            var historyService = App.GetService<ITriggerHistoryService>();
            var record = new TriggerExecutionRecord
            {
                TriggerName = triggerName,
                TriggerId = triggerId,
                TaskName = taskDefinitionName ?? "Unknown",
                StartTime = DateTime.Now,
                Status = TriggerExecutionStatus.Running,
                CorrelationId = correlationId
            };

            if (historyService != null)
            {
                await historyService.AddRecordAsync(record);
            }

            try
            {
                if (string.IsNullOrWhiteSpace(taskDefinitionName))
                {
                    _logger.LogWarning("触发器 {TriggerName} 未配置任务定义名称", triggerName);
                    if (historyService != null)
                    {
                        record.EndTime = DateTime.Now;
                        record.Status = TriggerExecutionStatus.Failed;
                        record.Message = "未配置任务定义名称";
                        await historyService.UpdateRecordAsync(record);
                    }
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
                
                if (historyService != null)
                {
                    record.EndTime = DateTime.Now;
                    record.Status = TriggerExecutionStatus.Success;
                    record.Message = "执行成功";
                    await historyService.UpdateRecordAsync(record);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行定时任务时发生错误");
                if (historyService != null)
                {
                    record.EndTime = DateTime.Now;
                    record.Status = TriggerExecutionStatus.Failed;
                    record.Message = ex.Message;
                    record.LogDetails = ex.ToString();
                    await historyService.UpdateRecordAsync(record);
                }
                throw;
            }
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