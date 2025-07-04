using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Group;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BetterGenshinImpact.Service.Quartz;

/// <summary>
/// 调度管理器 - 管理动态添加和删除定时任务
/// </summary>
public class SchedulerManager
{
    private readonly IScheduler _scheduler;
    private readonly ILogger<SchedulerManager> _logger;

    public SchedulerManager(IScheduler scheduler, ILogger<SchedulerManager> logger)
    {
        _scheduler = scheduler;
        _logger = logger;
    }

    /// <summary>
    /// 动态添加定时任务
    /// </summary>
    /// <param name="scriptGroup">脚本组</param>
    /// <param name="cronExpression">Cron表达式</param>
    /// <param name="jobName">任务名称（可选）</param>
    /// <returns>任务是否添加成功</returns>
    public async Task<bool> AddScheduledTaskAsync(ScriptGroup scriptGroup, string cronExpression, string? jobName = null)
    {
        try
        {
            jobName ??= $"ScriptGroup_{scriptGroup.Name}_{DateTime.Now.Ticks}";
            var jobKey = new JobKey(jobName, "ScriptGroup");
            var triggerKey = new TriggerKey($"{jobName}_trigger", "ScriptGroup");

            // 检查任务是否已存在
            if (await _scheduler.CheckExists(jobKey))
            {
                _logger.LogWarning("任务 {JobName} 已存在", jobName);
                return false;
            }

            // 创建任务
            var job = JobBuilder.Create<ScriptExecutionJob>()
                .WithIdentity(jobKey)
                .UsingJobData("ScriptGroupName", scriptGroup.Name)
                .UsingJobData("ScriptGroupData", scriptGroup.ToJson())
                .WithDescription($"脚本组 {scriptGroup.Name} 的定时任务")
                .Build();

            // 创建触发器
            var trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithCronSchedule(cronExpression)
                .WithDescription($"脚本组 {scriptGroup.Name} 的定时触发器")
                .Build();

            // 添加任务到调度器
            await _scheduler.ScheduleJob(job, trigger);
            
            _logger.LogInformation("成功添加定时任务：{JobName}，Cron表达式：{CronExpression}", jobName, cronExpression);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加定时任务失败：{Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 删除定时任务
    /// </summary>
    /// <param name="jobName">任务名称</param>
    /// <returns>任务是否删除成功</returns>
    public async Task<bool> RemoveScheduledTaskAsync(string jobName)
    {
        try
        {
            var jobKey = new JobKey(jobName, "ScriptGroup");
            
            if (!await _scheduler.CheckExists(jobKey))
            {
                _logger.LogWarning("任务 {JobName} 不存在", jobName);
                return false;
            }

            await _scheduler.DeleteJob(jobKey);
            _logger.LogInformation("成功删除定时任务：{JobName}", jobName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除定时任务失败：{Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 获取所有定时任务
    /// </summary>
    /// <returns>任务列表</returns>
    public async Task<List<ScheduledTaskInfo>> GetAllScheduledTasksAsync()
    {
        try
        {
            var jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("ScriptGroup"));
            var tasks = new List<ScheduledTaskInfo>();

            foreach (var jobKey in jobKeys)
            {
                var jobDetail = await _scheduler.GetJobDetail(jobKey);
                var triggers = await _scheduler.GetTriggersOfJob(jobKey);
                
                foreach (var trigger in triggers)
                {
                    var nextFireTime = trigger.GetNextFireTimeUtc();
                    var previousFireTime = trigger.GetPreviousFireTimeUtc();
                    
                    tasks.Add(new ScheduledTaskInfo
                    {
                        JobName = jobKey.Name,
                        ScriptGroupName = jobDetail?.JobDataMap.GetString("ScriptGroupName") ?? "",
                        CronExpression = trigger is ICronTrigger cronTrigger ? cronTrigger.CronExpressionString : "",
                        NextFireTime = nextFireTime?.DateTime,
                        PreviousFireTime = previousFireTime?.DateTime,
                        Description = jobDetail?.Description ?? ""
                    });
                }
            }

            return tasks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取定时任务列表失败：{Message}", ex.Message);
            return new List<ScheduledTaskInfo>();
        }
    }

    /// <summary>
    /// 更新定时任务的Cron表达式
    /// </summary>
    /// <param name="jobName">任务名称</param>
    /// <param name="newCronExpression">新的Cron表达式</param>
    /// <returns>任务是否更新成功</returns>
    public async Task<bool> UpdateScheduledTaskAsync(string jobName, string newCronExpression)
    {
        try
        {
            var jobKey = new JobKey(jobName, "ScriptGroup");
            var triggerKey = new TriggerKey($"{jobName}_trigger", "ScriptGroup");
            
            if (!await _scheduler.CheckExists(jobKey))
            {
                _logger.LogWarning("任务 {JobName} 不存在", jobName);
                return false;
            }

            // 创建新的触发器
            var newTrigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithCronSchedule(newCronExpression)
                .WithDescription($"更新的定时触发器")
                .Build();

            // 重新调度任务
            await _scheduler.RescheduleJob(triggerKey, newTrigger);
            
            _logger.LogInformation("成功更新定时任务：{JobName}，新的Cron表达式：{CronExpression}", jobName, newCronExpression);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新定时任务失败：{Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 暂停定时任务
    /// </summary>
    /// <param name="jobName">任务名称</param>
    /// <returns>任务是否暂停成功</returns>
    public async Task<bool> PauseScheduledTaskAsync(string jobName)
    {
        try
        {
            var jobKey = new JobKey(jobName, "ScriptGroup");
            
            if (!await _scheduler.CheckExists(jobKey))
            {
                _logger.LogWarning("任务 {JobName} 不存在", jobName);
                return false;
            }

            await _scheduler.PauseJob(jobKey);
            _logger.LogInformation("成功暂停定时任务：{JobName}", jobName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "暂停定时任务失败：{Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 恢复定时任务
    /// </summary>
    /// <param name="jobName">任务名称</param>
    /// <returns>任务是否恢复成功</returns>
    public async Task<bool> ResumeScheduledTaskAsync(string jobName)
    {
        try
        {
            var jobKey = new JobKey(jobName, "ScriptGroup");
            
            if (!await _scheduler.CheckExists(jobKey))
            {
                _logger.LogWarning("任务 {JobName} 不存在", jobName);
                return false;
            }

            await _scheduler.ResumeJob(jobKey);
            _logger.LogInformation("成功恢复定时任务：{JobName}", jobName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复定时任务失败：{Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 根据脚本组配置转换为Cron表达式
    /// </summary>
    /// <param name="schedule">调度配置</param>
    /// <returns>Cron表达式</returns>
    public static string ConvertScheduleToCron(string schedule)
    {
        return schedule switch
        {
            "Daily" => "0 0 0 * * ? *",              // 每天午夜执行
            "EveryTwoDays" => "0 0 0 1/2 * ? *",     // 每两天执行
            "Monday" => "0 0 0 ? * MON *",           // 每周一执行
            "Tuesday" => "0 0 0 ? * TUE *",          // 每周二执行
            "Wednesday" => "0 0 0 ? * WED *",        // 每周三执行
            "Thursday" => "0 0 0 ? * THU *",         // 每周四执行
            "Friday" => "0 0 0 ? * FRI *",           // 每周五执行
            "Saturday" => "0 0 0 ? * SAT *",         // 每周六执行
            "Sunday" => "0 0 0 ? * SUN *",           // 每周日执行
            _ => schedule                             // 假设是自定义Cron表达式
        };
    }
}

/// <summary>
/// 定时任务信息
/// </summary>
public class ScheduledTaskInfo
{
    public string JobName { get; set; } = "";
    public string ScriptGroupName { get; set; } = "";
    public string CronExpression { get; set; } = "";
    public DateTime? NextFireTime { get; set; }
    public DateTime? PreviousFireTime { get; set; }
    public string Description { get; set; } = "";
}