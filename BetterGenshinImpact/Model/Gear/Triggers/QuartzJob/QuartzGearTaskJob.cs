using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Tasks;
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
            var jobDataMap = context.JobDetail.JobDataMap;
            var taskListJson = jobDataMap.GetString("GearTaskReferenceList");
            var triggerName = jobDataMap.GetString("TriggerName") ?? "Unknown";
            var triggerId = jobDataMap.GetString("TriggerId") ?? Guid.NewGuid().ToString();

            if (string.IsNullOrEmpty(taskListJson))
            {
                _logger.LogWarning("触发器 {TriggerName} 没有配置任何任务", triggerName);
                return;
            }

            var gearTaskReferenceList = System.Text.Json.JsonSerializer.Deserialize<List<GearTaskRefence>>(taskListJson);
            if (gearTaskReferenceList == null || !gearTaskReferenceList.Any())
            {
                _logger.LogWarning("触发器 {TriggerName} 任务列表反序列化失败或为空", triggerName);
                return;
            }

            _logger.LogInformation("开始执行触发器 {TriggerName} 的任务，任务数量: {TaskCount}", triggerName, gearTaskReferenceList.Count);

            // 检查是否需要中断其他同类型任务
            var shouldInterrupt = jobDataMap.GetBooleanValue("ShouldInterruptOthers");
            if (shouldInterrupt)
            {
                await InterruptOtherJobs(context, triggerId);
            }

            // 执行任务列表
            var tasks = gearTaskReferenceList.Where(x => x.Enabled).Select(x => x.ToGearTask()).ToList();
            foreach (var task in tasks)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("任务执行被取消");
                    break;
                }

                try
                {
                    await task.Run(context.CancellationToken);
                    _logger.LogDebug("任务 {TaskName} 执行完成", task.Name);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("任务 {TaskName} 被取消", task.Name);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "任务 {TaskName} 执行失败", task.Name);
                    // 继续执行下一个任务，不中断整个流程
                }
            }

            _logger.LogInformation("触发器 {TriggerName} 的任务执行完成", triggerName);
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